// The editor tests run the production patch without loading game or Unity DLLs.
using AncientWarfare3.core.naming;

namespace UnityEngine
{
    internal class Object
    {
        public static T Instantiate<T>(T source, Transform parent) where T : Object
            => throw new NotSupportedException("Tests provide both input fields.");
    }

    internal class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public string name { get => gameObject.name; set => gameObject.name = value; }
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }

    internal class MonoBehaviour : Component { }

    internal class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();
        public string name;
        public Transform transform;
        public bool activeSelf = true;

        public GameObject(string objectName, params Type[] types)
        {
            name = objectName;
            transform = (Transform)AddComponent(types.Contains(typeof(RectTransform))
                ? typeof(RectTransform) : typeof(Transform));
            foreach (Type type in types)
                if (type != typeof(RectTransform)) AddComponent(type);
        }

        private Component AddComponent(Type type)
        {
            var component = (Component)Activator.CreateInstance(type);
            component.gameObject = this;
            _components.Add(component);
            return component;
        }

        public T AddComponent<T>() where T : Component => (T)AddComponent(typeof(T));
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
        public void SetActive(bool active)
        {
            if (activeSelf == active) return;
            activeSelf = active;
            foreach (NameInput input in _components.OfType<NameInput>())
            {
                if (active) input.OnEnable();
                else input.OnDisable();
            }
        }
    }

    internal class Transform : Component
    {
        private readonly List<Transform> _children = new List<Transform>();
        public Transform parent;
        public Vector2 localScale;
        public Transform Find(string childName) => _children.FirstOrDefault(c => c.name == childName);
        public void SetParent(Transform target, bool worldPositionStays)
        {
            parent?._children.Remove(this);
            parent = target;
            parent?._children.Add(this);
        }
    }

    internal sealed class RectTransform : Transform
    {
        public Vector2 sizeDelta, anchoredPosition, anchorMin, anchorMax, pivot;
        public Rect rect = new Rect();
    }

    internal struct Rect { public float width; }
    internal struct Vector2
    {
        public float x, y;
        public Vector2(float xValue, float yValue) { x = xValue; y = yValue; }
        public static Vector2 left => new Vector2(-1, 0);
        public static Vector2 right => new Vector2(1, 0);
        public static Vector2 operator +(Vector2 left, Vector2 right) => new Vector2(left.x + right.x, left.y + right.y);
        public static Vector2 operator *(Vector2 vector, float scale) => new Vector2(vector.x * scale, vector.y * scale);
    }

    internal enum TextAnchor { LowerLeft }
}

namespace UnityEngine.UI
{
    internal sealed class Text : UnityEngine.Component
    {
        public string text;
        public object font, color;
        public int fontSize;
        public UnityEngine.TextAnchor alignment;
        public bool raycastTarget;
        public UnityEngine.RectTransform rectTransform => GetComponent<UnityEngine.RectTransform>();
    }

    internal sealed class InputEvent
    {
        private event Action<string> Listeners;
        public void RemoveAllListeners() => Listeners = null;
        public void AddListener(Action<string> listener) => Listeners += listener;
        public void Invoke(string text) => Listeners?.Invoke(text);
    }
}

namespace UnityEngine.EventSystems
{
    internal sealed class PointerEventData { }
    internal interface IPointerClickHandler { void OnPointerClick(PointerEventData data); }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    internal sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string name) { }
    }
    internal sealed class HarmonyPostfix : Attribute { }
    internal sealed class HarmonyPriority : Attribute { public HarmonyPriority(int priority) { } }
    internal static class Priority { public const int Last = 0; }
}

internal sealed class NameInput : UnityEngine.Component
{
    public UnityEngine.UI.InputField inputField;
    public UnityEngine.UI.Text textField;
    public bool can_be_empty;
    public string LastValue;
    public void SetOutline() { }
    public void OnEnable() => inputField.onEndEdit.AddListener(CheckInput);
    public void OnDisable() => inputField.onEndEdit.RemoveAllListeners();
    private void CheckInput(string text)
    {
        if (string.IsNullOrWhiteSpace(text) && !can_be_empty)
            inputField.text = LastValue;
        else LastValue = text;
    }
}

internal sealed class ActorData { public long id; public bool custom_name; }
internal sealed class Actor
{
    public ActorData data = new ActorData { id = 1 };
    public string Given = "Alexander", Family = "Macedon";
    public ActorManualNameMode Mode = ActorManualNameMode.NonXia;
    public bool isRekt() => false;
    public string getName() => ActorManualRenameRules.UsesFamilyFirst(Mode)
        ? Family + Given : Given + " " + Family;
}
internal sealed class UnitWindow { public Actor actor; public NameInput name_input; }
internal static class WorldTip
{
    public static void showNow(string text, bool pTranslate, string position) { }
}

namespace AncientWarfare3.ui
{
    internal static class AW_L10n { public static string Text(string key, string fallback) => fallback; }
}

namespace AncientWarfare3.core.naming
{
    internal static class ActorManualRenameRules
    {
        internal static bool UsesFamilyFirst(ActorManualNameMode mode) => mode != ActorManualNameMode.NonXia;
        internal static bool UsesSingleMergedShi(ActorManualNameMode mode) => mode == ActorManualNameMode.SiniticMerged;
    }

    internal static class ActorManualRenameService
    {
        internal static ActorManualNameMode ResolveMode(Actor actor) => actor.Mode;
        internal static ActorManualNameDraft Capture(Actor actor) => ActorManualRenameRules.UsesFamilyFirst(actor.Mode)
            ? ActorManualNameRules.CreateDraft(actor.Mode, actor.Family, actor.Given)
            : ActorManualNameRules.CreateDraft(actor.Mode, actor.Given, actor.Family);
        internal static bool TryCommit(Actor actor, string first, string second, out string error)
        {
            ActorManualNameDraft draft = ActorManualNameRules.CreateDraft(ResolveMode(actor), first, second);
            error = draft.IsValid ? string.Empty : "given_name_empty";
            if (!draft.IsValid) return false;
            actor.Given = draft.GivenName;
            actor.Family = draft.FamilyOrClanName;
            return true;
        }
    }
}
