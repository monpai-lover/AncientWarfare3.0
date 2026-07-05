using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.policy
{
    internal sealed class AWMapModeMetaObject : MetaObject<MetaObjectData>
    {
        private readonly MetaType _metaType;
        private readonly ColorAsset _color;
        private readonly ActorAsset _actorAsset;

        public AWMapModeMetaObject(long pId, string pName, MetaType pMetaType, ColorAsset pColor)
        {
            _metaType = pMetaType;
            _color = pColor;
            _actorAsset = AssetManager.actor_library.get("human");
            data = new MetaObjectData
            {
                id = pId,
                name = pName ?? "",
                created_time = 0
            };
            setHash((pMetaType.GetHashCode() * 397) ^ pId.GetHashCode());
            _color?.initColor();
        }

        public override MetaType meta_type => _metaType;

        public override BaseSystemManager manager => null;

        public override ColorLibrary getColorLibrary()
        {
            return AssetManager.kingdom_colors_library;
        }

        public override ColorAsset getColor()
        {
            return _color ?? ColorAsset.tryMakeNewColorAsset("#777777");
        }

        public override ActorAsset getActorAsset()
        {
            return _actorAsset ?? AssetManager.actor_library.get("human");
        }

        public override bool hasCities()
        {
            return false;
        }

        public override IEnumerable<City> getCities()
        {
            return new City[0];
        }

        public override bool hasKingdoms()
        {
            return false;
        }

        public override IEnumerable<Kingdom> getKingdoms()
        {
            return new Kingdom[0];
        }

        public override Sprite getTopicSprite()
        {
            return getSpriteIcon();
        }

        public override void Dispose()
        {
        }
    }
}
