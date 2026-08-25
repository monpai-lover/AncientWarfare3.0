using System;

namespace AncientWarfare3.core.pathfinding
{
    public enum AWPathWorldKind : byte
    {
        MainWorld,
        SubWorld
    }

    public readonly struct AWPathWorldKey : IEquatable<AWPathWorldKey>
    {
        public AWPathWorldKey(AWPathWorldKind pKind, long pInstanceId,
            long pGeneration)
        {
            Kind = pKind;
            InstanceId = pInstanceId;
            Generation = pGeneration;
        }

        public AWPathWorldKind Kind { get; }
        public long InstanceId { get; }
        public long Generation { get; }

        public static AWPathWorldKey MainWorld(long pGeneration)
        {
            return new AWPathWorldKey(AWPathWorldKind.MainWorld, 0L,
                pGeneration);
        }

        public static AWPathWorldKey SubWorld(long pInstanceId,
            long pGeneration)
        {
            return new AWPathWorldKey(AWPathWorldKind.SubWorld,
                pInstanceId, pGeneration);
        }

        public bool Equals(AWPathWorldKey pOther)
        {
            return Kind == pOther.Kind && InstanceId == pOther.InstanceId &&
                   Generation == pOther.Generation;
        }

        public override bool Equals(object pObject)
        {
            return pObject is AWPathWorldKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = hash * 397 ^ InstanceId.GetHashCode();
                return hash * 397 ^ Generation.GetHashCode();
            }
        }

        public static bool operator ==(AWPathWorldKey pLeft,
            AWPathWorldKey pRight) => pLeft.Equals(pRight);

        public static bool operator !=(AWPathWorldKey pLeft,
            AWPathWorldKey pRight) => !pLeft.Equals(pRight);

        public override string ToString()
        {
            return $"{Kind}:{InstanceId}:{Generation}";
        }
    }

    public readonly struct AWPathAgentKey : IEquatable<AWPathAgentKey>
    {
        public AWPathAgentKey(AWPathWorldKey pWorld, long pAgentId)
        {
            World = pWorld;
            AgentId = pAgentId;
        }

        public AWPathWorldKey World { get; }
        public long AgentId { get; }
        // Actor ids are positive; AW3's shared army-route namespace uses
        // negative ids so it cannot collide with a captain actor request.
        public bool IsValid => AgentId != 0L;

        public bool Equals(AWPathAgentKey pOther)
        {
            return World.Equals(pOther.World) && AgentId == pOther.AgentId;
        }

        public override bool Equals(object pObject)
        {
            return pObject is AWPathAgentKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return World.GetHashCode() * 397 ^ AgentId.GetHashCode();
            }
        }

        public static bool operator ==(AWPathAgentKey pLeft,
            AWPathAgentKey pRight) => pLeft.Equals(pRight);

        public static bool operator !=(AWPathAgentKey pLeft,
            AWPathAgentKey pRight) => !pLeft.Equals(pRight);

        public override string ToString()
        {
            return $"{World}/agent:{AgentId}";
        }
    }

    public readonly struct AWPathHandle : IEquatable<AWPathHandle>
    {
        public AWPathHandle(AWPathAgentKey pAgent, long pSubmissionToken)
        {
            Agent = pAgent;
            SubmissionToken = pSubmissionToken;
        }

        public AWPathAgentKey Agent { get; }
        public long SubmissionToken { get; }
        public bool IsValid => Agent.IsValid && SubmissionToken > 0L;

        public bool Equals(AWPathHandle pOther)
        {
            return Agent.Equals(pOther.Agent) &&
                   SubmissionToken == pOther.SubmissionToken;
        }

        public override bool Equals(object pObject)
        {
            return pObject is AWPathHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return Agent.GetHashCode() * 397 ^
                       SubmissionToken.GetHashCode();
            }
        }

        public static bool operator ==(AWPathHandle pLeft,
            AWPathHandle pRight) => pLeft.Equals(pRight);

        public static bool operator !=(AWPathHandle pLeft,
            AWPathHandle pRight) => !pLeft.Equals(pRight);
    }
}
