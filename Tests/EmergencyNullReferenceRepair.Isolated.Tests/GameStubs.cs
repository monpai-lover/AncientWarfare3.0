// The fields and safe methods below match Assembly-CSharp.dll. In particular,
// id dereferences data, native container Remove is deferred, and task finish
// does not call the dangerous clearBeh/task-switch delegates.
public sealed class ObjectData { public long id; }
public sealed class TileData { public int tile_id; }
public class BaseSimObject
{
    public Actor a;
    public Building b;
    public bool alive = true;
    public WorldTile current_tile;
    public bool isAlive() => alive;
}
public sealed class Actor : BaseSimObject
{
    public ObjectData data;
    public long id => data.id;
    public object asset = new();
    public AiSystemActor ai = new();
    public BatchActors batch;
    public BaseSimObject attack_target, attackedBy, beh_actor_target;
    public Building beh_building_target;
    public Book beh_book_target;
    public WorldTile beh_tile_target, tile_target, scheduled_tile_target, _next_step_tile;
    public readonly List<WorldTile> current_path = new();
    public List<MapRegion> current_path_global;
    public int current_path_index;
    public bool has_attack_target, _is_moving, is_inside_boat, dirty_current_tile;
    public SplitPathStatus split_path;
    public float timer_action;
    public object army = new(), inside_building = new(), inside_boat;
    public Actor(long actorId) { data = new() { id = actorId }; a = this; }
    public void clearOldPath() { current_path.Clear(); current_path_global = null; current_path_index = 0; }
    public void clearAttackTarget()
    {
        if (!has_attack_target) return;
        attack_target = null; has_attack_target = false; batch.c_check_attack_target.Remove(a);
    }
    public void cancelAllBeh() => throw new Exception("Unsafe behaviour callback invoked");
    public void stopMovement() => throw new Exception("Unsafe movement lifecycle invoked");
}
public sealed class Building : BaseSimObject
{
    public ObjectData data = new();
    public object asset = new();
    public Building() { b = this; }
}
public sealed class Book { public ObjectData data = new(); }
public sealed class WorldTile
{
    public TileData data;
    public object Type = new();
    public Actor _targeted_by;
    public WorldTile(int id) { data = new() { tile_id = id }; }
    public void cleanTargetedBy() => _targeted_by = null;
}
public sealed class MapRegion { }
public enum SplitPathStatus { Normal, Split }
public sealed class AiSystemActor
{
    public object job = new(), action = new();
    public BehaviourTaskActor task = new();
    public int action_index = 3, task_index = 2, Finished;
    public void setTaskBehFinished() { task = null; action_index = -1; action = null; Finished++; }
}
public sealed class BehaviourTaskActor { public bool in_combat; }
public sealed class ObjectContainer<T>
{
    public HashSet<T> _hashSet = new(), _to_add = new(), _to_remove = new();
    public bool _dirty_list, _dirty_array, _dirty_container;
    public readonly List<T> _simple_list = new();
    public T[] _array = Array.Empty<T>();
    public int _array_count;
    public void Add(T value) { _to_add.Add(value); _to_remove.Remove(value); _dirty_container = true; }
    public void Remove(T value) { _to_remove.Add(value); _to_add.Remove(value); _dirty_container = true; }
    public void checkAddRemove()
    {
        if (_to_add.Count > 0)
        {
            if (_hashSet.Count == 0) { var old = _hashSet; _hashSet = _to_add; _to_add = old; }
            else { _hashSet.UnionWith(_to_add); _to_add.Clear(); }
            _dirty_list = _dirty_array = true;
        }
        if (_to_remove.Count > 0)
        {
            _hashSet.ExceptWith(_to_remove); _to_remove.Clear();
            _dirty_list = _dirty_array = true;
        }
        _dirty_container = false;
    }
    public List<T> getSimpleList()
    {
        if (_dirty_list) { _simple_list.Clear(); _simple_list.AddRange(_hashSet); _dirty_list = false; }
        return _simple_list;
    }
    public T[] getSimpleArray()
    {
        if (_dirty_array) { _array = _hashSet.ToArray(); _array_count = _hashSet.Count; _dirty_array = false; }
        return _array;
    }
}
public sealed class BatchActors
{
    public ObjectContainer<Actor> c_check_attack_target = new(), c_update_movement = new(), main = new();
    public List<ObjectContainer<Actor>> containers;
    public List<Actor> l_parallel_update_sprites = new();
    public BatchActors() { containers = new() { main, c_check_attack_target, c_update_movement }; }
}
public sealed class JobManagerActors
{
    public readonly List<BatchActors> _batches_active = new();
    public readonly Stack<BatchActors> _batches_free = new();
}
public sealed class ActorManager
{
    public readonly Dictionary<long, Actor> dict = new();
    public JobManagerActors _job_manager = new();
}
public sealed class MapBox { public ActorManager units = new(); public WorldTile[] tiles_list; }
public static class World { public static MapBox world; }
