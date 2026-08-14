using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.presentation
{
    public static class ArmyRtsPlanRasterizer
    {
        public static readonly ArmyRtsPlanColor LandColor =
            new ArmyRtsPlanColor(54, 58, 56);
        public static readonly ArmyRtsPlanColor WaterColor =
            new ArmyRtsPlanColor(24, 58, 105);
        public static readonly ArmyRtsPlanColor ParticipantBorderColor =
            new ArmyRtsPlanColor(245, 245, 225);
        public static readonly ArmyRtsPlanColor MarkerColor =
            new ArmyRtsPlanColor(245, 245, 245);
        public static readonly ArmyRtsPlanColor AttackColor =
            new ArmyRtsPlanColor(235, 45, 45);
        public static readonly ArmyRtsPlanColor RecoveryColor =
            new ArmyRtsPlanColor(255, 210, 35);
        public static readonly ArmyRtsPlanColor RedeployColor =
            new ArmyRtsPlanColor(45, 110, 235);
        public static readonly ArmyRtsPlanColor TransportColor =
            new ArmyRtsPlanColor(25, 220, 235);
        public static readonly ArmyRtsPlanColor MarchColor =
            new ArmyRtsPlanColor(245, 245, 245);
        public static readonly ArmyRtsPlanColor StalledColor =
            new ArmyRtsPlanColor(235, 50, 210);

        public static ArmyRtsPlanIndexedRaster Render(
            ArmyRtsPlanSnapshot pSnapshot,
            int maximumLongEdge = ArmyRtsPlanRules.DefaultMaximumLongEdge)
        {
            if (pSnapshot == null)
                throw new ArgumentNullException(nameof(pSnapshot));
            ArmyRtsPlanCanvas canvas;
            byte[] pixels;
            if (pSnapshot.Terrain != null)
            {
                canvas = new ArmyRtsPlanCanvas(pSnapshot.Terrain.Width,
                    pSnapshot.Terrain.Height, pSnapshot.WorldWidth,
                    pSnapshot.WorldHeight);
                pixels = (byte[])pSnapshot.Terrain.Pixels.Clone();
            }
            else
            {
                canvas = ArmyRtsPlanRules.Project(pSnapshot.WorldWidth,
                    pSnapshot.WorldHeight, maximumLongEdge);
                pixels = new byte[canvas.Width * canvas.Height];
                Fill(pixels, Index(LandColor));
                DrawZones(pixels, canvas, pSnapshot.Zones);
            }
            DrawFronts(pixels, canvas, pSnapshot.Fronts);
            DrawCities(pixels, canvas, pSnapshot.Cities);
            DrawArmies(pixels, canvas, pSnapshot.Armies);
            return new ArmyRtsPlanIndexedRaster(canvas.Width,
                canvas.Height, pixels);
        }

        public static ArmyRtsPlanColor ColorFor(
            ArmyRtsPlanArrowStyle pStyle)
        {
            switch (pStyle)
            {
                case ArmyRtsPlanArrowStyle.Attack: return AttackColor;
                case ArmyRtsPlanArrowStyle.Recovery: return RecoveryColor;
                case ArmyRtsPlanArrowStyle.Redeploy: return RedeployColor;
                case ArmyRtsPlanArrowStyle.Transport: return TransportColor;
                default: return MarchColor;
            }
        }

        private static void DrawZones(byte[] pPixels,
            ArmyRtsPlanCanvas pCanvas,
            IReadOnlyList<ArmyRtsPlanZone> pZones)
        {
            for (int i = 0; i < pZones.Count; i++)
            {
                ArmyRtsPlanZone zone = pZones[i];
                ArmyRtsPlanPoint first = pCanvas.ProjectPoint(
                    new ArmyRtsPlanPoint(zone.X, zone.Y));
                ArmyRtsPlanPoint last = pCanvas.ProjectPoint(
                    new ArmyRtsPlanPoint(zone.X + zone.Width - 1,
                        zone.Y + zone.Height - 1));
                int left = Math.Min(first.X, last.X);
                int right = Math.Max(first.X, last.X);
                int top = Math.Min(first.Y, last.Y);
                int bottom = Math.Max(first.Y, last.Y);
                ArmyRtsPlanColor color = zone.Water
                    ? WaterColor
                    : zone.KingdomId >= 0L ? Muted(zone.Color) : LandColor;
                FillRect(pPixels, pCanvas.Width, pCanvas.Height, left, top,
                    right, bottom, Index(color));
                if (zone.Participant)
                    DrawRect(pPixels, pCanvas.Width, pCanvas.Height, left,
                        top, right, bottom, Index(ParticipantBorderColor));
            }
        }

        private static void DrawFronts(byte[] pPixels,
            ArmyRtsPlanCanvas pCanvas,
            IReadOnlyList<ArmyRtsPlanFront> pFronts)
        {
            byte color = Index(ParticipantBorderColor);
            for (int i = 0; i < pFronts.Count; i++)
                DrawLine(pPixels, pCanvas.Width, pCanvas.Height,
                    pCanvas.ProjectPoint(pFronts[i].Start),
                    pCanvas.ProjectPoint(pFronts[i].End), color, 2, false);
        }

        private static void DrawCities(byte[] pPixels,
            ArmyRtsPlanCanvas pCanvas,
            IReadOnlyList<ArmyRtsPlanCity> pCities)
        {
            for (int i = 0; i < pCities.Count; i++)
            {
                ArmyRtsPlanCity city = pCities[i];
                ArmyRtsPlanPoint point = pCanvas.ProjectPoint(city.Position);
                byte color = Index(city.FriendlyOccupied
                    ? RecoveryColor
                    : MarkerColor);
                FillRect(pPixels, pCanvas.Width, pCanvas.Height,
                    point.X - 2, point.Y - 2, point.X + 2, point.Y + 2,
                    color);
            }
        }

        private static void DrawArmies(byte[] pPixels,
            ArmyRtsPlanCanvas pCanvas,
            IReadOnlyList<ArmyRtsPlanArmy> pArmies)
        {
            for (int i = 0; i < pArmies.Count; i++)
            {
                ArmyRtsPlanArmy army = pArmies[i];
                ArmyRtsPlanPoint captain = pCanvas.ProjectPoint(
                    army.Captain);
                ArmyRtsPlanPoint target = pCanvas.ProjectPoint(army.Target);
                ArmyRtsPlanPoint anchor = army.RouteAnchor.HasValue
                    ? pCanvas.ProjectPoint(army.RouteAnchor.Value)
                    : target;
                ArmyRtsPlanArrowStyle style = ArmyRtsPlanRules.ArrowStyle(
                    army);
                byte color = Index(ColorFor(style));
                int thickness = style == ArmyRtsPlanArrowStyle.Recovery
                    ? 2
                    : 1;
                ArmyRtsPlanPoint routeCursor = captain;
                ArmyRtsPlanPoint arrowStart = captain;
                for (int pathIndex = 0;
                     pathIndex < army.ActualPath.Count; pathIndex++)
                {
                    ArmyRtsPlanPoint point = pCanvas.ProjectPoint(
                        army.ActualPath[pathIndex]);
                    if (point == routeCursor) continue;
                    DrawLine(pPixels, pCanvas.Width, pCanvas.Height,
                        routeCursor, point, color, thickness, false);
                    arrowStart = routeCursor;
                    routeCursor = point;
                }
                if (anchor != routeCursor)
                {
                    DrawLine(pPixels, pCanvas.Width, pCanvas.Height,
                        routeCursor, anchor, color, thickness, true);
                    arrowStart = routeCursor;
                }
                if (target != anchor)
                {
                    DrawLine(pPixels, pCanvas.Width, pCanvas.Height,
                        anchor, target, color, thickness, true);
                    arrowStart = anchor;
                }
                DrawArrowHead(pPixels, pCanvas.Width, pCanvas.Height,
                    arrowStart, target, color, thickness);
                DrawMarker(pPixels, pCanvas.Width, pCanvas.Height, captain,
                    color);
                if (army.Stalled)
                    DrawStalledMarker(pPixels, pCanvas.Width,
                        pCanvas.Height, captain, Index(StalledColor));
                if (style == ArmyRtsPlanArrowStyle.Transport)
                    DrawShipMarker(pPixels, pCanvas.Width, pCanvas.Height,
                        new ArmyRtsPlanPoint((captain.X + target.X) / 2,
                            (captain.Y + target.Y) / 2), color);
            }
        }

        private static void DrawArrowHead(byte[] pPixels, int pWidth,
            int pHeight, ArmyRtsPlanPoint pStart, ArmyRtsPlanPoint pEnd,
            byte pColor, int pThickness)
        {
            double dx = pEnd.X - pStart.X;
            double dy = pEnd.Y - pStart.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1d) return;
            double ux = dx / length;
            double uy = dy / length;
            var left = new ArmyRtsPlanPoint(
                (int)Math.Round(pEnd.X - ux * 6d - uy * 3.5d),
                (int)Math.Round(pEnd.Y - uy * 6d + ux * 3.5d));
            var right = new ArmyRtsPlanPoint(
                (int)Math.Round(pEnd.X - ux * 6d + uy * 3.5d),
                (int)Math.Round(pEnd.Y - uy * 6d - ux * 3.5d));
            DrawLine(pPixels, pWidth, pHeight, pEnd, left, pColor,
                pThickness, false);
            DrawLine(pPixels, pWidth, pHeight, pEnd, right, pColor,
                pThickness, false);
        }

        private static void DrawMarker(byte[] pPixels, int pWidth,
            int pHeight, ArmyRtsPlanPoint pPoint, byte pColor)
        {
            SetPixel(pPixels, pWidth, pHeight, pPoint.X, pPoint.Y, pColor);
            SetPixel(pPixels, pWidth, pHeight, pPoint.X - 1, pPoint.Y,
                pColor);
            SetPixel(pPixels, pWidth, pHeight, pPoint.X + 1, pPoint.Y,
                pColor);
            SetPixel(pPixels, pWidth, pHeight, pPoint.X, pPoint.Y - 1,
                pColor);
            SetPixel(pPixels, pWidth, pHeight, pPoint.X, pPoint.Y + 1,
                pColor);
        }

        private static void DrawShipMarker(byte[] pPixels, int pWidth,
            int pHeight, ArmyRtsPlanPoint pPoint, byte pColor)
        {
            DrawLine(pPixels, pWidth, pHeight,
                new ArmyRtsPlanPoint(pPoint.X - 3, pPoint.Y + 1),
                new ArmyRtsPlanPoint(pPoint.X + 3, pPoint.Y + 1), pColor,
                1, false);
            DrawLine(pPixels, pWidth, pHeight,
                new ArmyRtsPlanPoint(pPoint.X - 2, pPoint.Y + 2),
                new ArmyRtsPlanPoint(pPoint.X + 2, pPoint.Y + 2), pColor,
                1, false);
            DrawLine(pPixels, pWidth, pHeight,
                new ArmyRtsPlanPoint(pPoint.X, pPoint.Y - 3),
                new ArmyRtsPlanPoint(pPoint.X, pPoint.Y + 1), pColor,
                1, false);
        }

        private static void DrawStalledMarker(byte[] pPixels, int pWidth,
            int pHeight, ArmyRtsPlanPoint pPoint, byte pColor)
        {
            SetPixel(pPixels, pWidth, pHeight, pPoint.X - 2, pPoint.Y - 2,
                pColor);
            SetPixel(pPixels, pWidth, pHeight, pPoint.X + 2, pPoint.Y - 2,
                pColor);
            SetPixel(pPixels, pWidth, pHeight, pPoint.X - 2, pPoint.Y + 2,
                pColor);
            SetPixel(pPixels, pWidth, pHeight, pPoint.X + 2, pPoint.Y + 2,
                pColor);
        }

        private static void DrawLine(byte[] pPixels, int pWidth,
            int pHeight, ArmyRtsPlanPoint pStart, ArmyRtsPlanPoint pEnd,
            byte pColor, int pThickness, bool pDashed)
        {
            int x0 = pStart.X;
            int y0 = pStart.Y;
            int x1 = pEnd.X;
            int y1 = pEnd.Y;
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;
            int step = 0;
            while (true)
            {
                if (!pDashed || step % 10 < 6)
                    DrawThickPixel(pPixels, pWidth, pHeight, x0, y0,
                        pColor, pThickness);
                if (x0 == x1 && y0 == y1) break;
                int twice = 2 * error;
                if (twice >= dy) { error += dy; x0 += sx; }
                if (twice <= dx) { error += dx; y0 += sy; }
                step++;
            }
        }

        private static void DrawThickPixel(byte[] pPixels, int pWidth,
            int pHeight, int pX, int pY, byte pColor, int pThickness)
        {
            int radius = Math.Max(0, pThickness - 1);
            for (int y = pY - radius; y <= pY + radius; y++)
                for (int x = pX - radius; x <= pX + radius; x++)
                    SetPixel(pPixels, pWidth, pHeight, x, y, pColor);
        }

        private static void FillRect(byte[] pPixels, int pWidth,
            int pHeight, int pLeft, int pTop, int pRight, int pBottom,
            byte pColor)
        {
            int left = Math.Max(0, Math.Min(pWidth - 1, pLeft));
            int right = Math.Max(0, Math.Min(pWidth - 1, pRight));
            int top = Math.Max(0, Math.Min(pHeight - 1, pTop));
            int bottom = Math.Max(0, Math.Min(pHeight - 1, pBottom));
            if (left > right || top > bottom) return;
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                    pPixels[y * pWidth + x] = pColor;
        }

        private static void DrawRect(byte[] pPixels, int pWidth,
            int pHeight, int pLeft, int pTop, int pRight, int pBottom,
            byte pColor)
        {
            DrawLine(pPixels, pWidth, pHeight,
                new ArmyRtsPlanPoint(pLeft, pTop),
                new ArmyRtsPlanPoint(pRight, pTop), pColor, 1, false);
            DrawLine(pPixels, pWidth, pHeight,
                new ArmyRtsPlanPoint(pRight, pTop),
                new ArmyRtsPlanPoint(pRight, pBottom), pColor, 1, false);
            DrawLine(pPixels, pWidth, pHeight,
                new ArmyRtsPlanPoint(pRight, pBottom),
                new ArmyRtsPlanPoint(pLeft, pBottom), pColor, 1, false);
            DrawLine(pPixels, pWidth, pHeight,
                new ArmyRtsPlanPoint(pLeft, pBottom),
                new ArmyRtsPlanPoint(pLeft, pTop), pColor, 1, false);
        }

        private static void SetPixel(byte[] pPixels, int pWidth,
            int pHeight, int pX, int pY, byte pColor)
        {
            if (pX < 0 || pX >= pWidth || pY < 0 || pY >= pHeight)
                return;
            pPixels[pY * pWidth + pX] = pColor;
        }

        private static void Fill(byte[] pPixels, byte pColor)
        {
            for (int i = 0; i < pPixels.Length; i++) pPixels[i] = pColor;
        }

        private static byte Index(ArmyRtsPlanColor pColor)
        {
            return ArmyRtsPlanPalette.IndexOf(pColor);
        }

        private static ArmyRtsPlanColor Muted(ArmyRtsPlanColor pColor)
        {
            return new ArmyRtsPlanColor(
                (byte)((pColor.Red + LandColor.Red * 2) / 3),
                (byte)((pColor.Green + LandColor.Green * 2) / 3),
                (byte)((pColor.Blue + LandColor.Blue * 2) / 3));
        }
    }
}
