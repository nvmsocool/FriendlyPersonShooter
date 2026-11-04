using System;
using FriendlyPersonShooter.Core.Combat;

namespace FriendlyPersonShooter.Core.Progression
{
    /// <summary>The skills, three per branch. Each skill needs one rank in the skill before it in its branch.</summary>
    public enum SkillId : byte
    {
        // Offense
        Sharpshooter = 0,
        RapidFire = 1,
        LongBarrel = 2,
        // Support
        Potency = 3,
        Lingering = 4,
        SecondWind = 5,
        // Survival
        Toughness = 6,
        FleetFoot = 7,
        Spring = 8,
    }

    public enum SkillBranch : byte
    {
        Offense = 0,
        Support = 1,
        Survival = 2,
    }

    public readonly struct SkillInfo
    {
        public readonly SkillId Id;
        public readonly SkillBranch Branch;
        public readonly string Name;
        /// <summary>What one rank does, e.g. "+10% damage".</summary>
        public readonly string PerRank;

        public SkillInfo(SkillId id, SkillBranch branch, string name, string perRank)
        {
            Id = id;
            Branch = branch;
            Name = name;
            PerRank = perRank;
        }

        /// <summary>The skill this one needs a rank in first (none for the first skill of a branch).</summary>
        public bool TryGetRequirement(out SkillId required)
        {
            int index = (int)Id % SkillTree.PerBranch;
            required = (SkillId)((int)Id - 1);
            return index > 0;
        }
    }

    /// <summary>The skill tree's shape and numbers.</summary>
    public static class SkillTree
    {
        public const int Count = 9;
        public const int PerBranch = 3;
        public const int MaxRank = 3;
        public const int MaxLevel = 30;

        public const float DamagePerRank = 0.10f;
        public const float FireRatePerRank = 0.10f;
        public const float RangePerRank = 0.20f;
        public const float PotencyPerRank = 0.15f;
        public const float DurationPerRank = 0.20f;
        /// <summary>Health per second per rank of Second Wind, after <see cref="RegenDelay"/> seconds unhurt.</summary>
        public const float RegenPerRank = 1.5f;
        public const float RegenDelay = 4f;
        public const float HealthPerRank = 15f;
        public const float MovePerRank = 0.06f;
        public const float JumpPerRank = 0.15f;

        public static readonly SkillInfo[] All =
        {
            new SkillInfo(SkillId.Sharpshooter, SkillBranch.Offense, "Sharpshooter", "+10% damage"),
            new SkillInfo(SkillId.RapidFire, SkillBranch.Offense, "Rapid Fire", "+10% fire rate"),
            new SkillInfo(SkillId.LongBarrel, SkillBranch.Offense, "Long Barrel", "+20% range"),
            new SkillInfo(SkillId.Potency, SkillBranch.Support, "Potency", "+15% heal and buff strength"),
            new SkillInfo(SkillId.Lingering, SkillBranch.Support, "Lingering", "+20% buff duration"),
            new SkillInfo(SkillId.SecondWind, SkillBranch.Support, "Second Wind", "+1.5 health/s after 4 s unhurt"),
            new SkillInfo(SkillId.Toughness, SkillBranch.Survival, "Toughness", "+15 max health"),
            new SkillInfo(SkillId.FleetFoot, SkillBranch.Survival, "Fleet Foot", "+6% move speed"),
            new SkillInfo(SkillId.Spring, SkillBranch.Survival, "Spring", "+15% jump"),
        };

        /// <summary>XP from the current level to the next: 100, 150, 200, ...</summary>
        public static int XpToNext(int level) => 100 + 50 * Math.Max(0, level - 1);
    }

    /// <summary>XP for things worth rewarding.</summary>
    public static class XpRewards
    {
        public static int Kill(EnemyFamily family)
        {
            switch (family)
            {
                case EnemyFamily.Wisp: return 20;
                case EnemyFamily.Skitter: return 30;
                case EnemyFamily.Glimmer: return 4;
                default: return 5;
            }
        }

        /// <summary>A buff or heal that landed on an ally.</summary>
        public const int AllyBuff = 2;
        /// <summary>Per essence banked at a shrine.</summary>
        public const int PerEssenceBanked = 2;
        public const int Revive = 25;
    }

    /// <summary>One player's level, XP, unspent skill points and skill ranks.</summary>
    public sealed class PlayerProgress
    {
        private readonly byte[] _ranks = new byte[SkillTree.Count];

        public int Level { get; private set; } = 1;
        /// <summary>XP towards the next level.</summary>
        public int Xp { get; private set; }
        public int Points { get; private set; }
        public int XpToNext => SkillTree.XpToNext(Level);

        public int Rank(SkillId id) => _ranks[(int)id];

        /// <summary>Adds XP; returns how many levels were gained (each gives a skill point).</summary>
        public int AddXp(int amount)
        {
            if (amount <= 0 || Level >= SkillTree.MaxLevel) return 0;
            Xp += amount;
            int gained = 0;
            while (Level < SkillTree.MaxLevel && Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                Points++;
                gained++;
            }
            if (Level >= SkillTree.MaxLevel) Xp = 0;
            return gained;
        }

        public bool CanBuy(SkillId id)
        {
            if ((int)id < 0 || (int)id >= SkillTree.Count || Points <= 0 || Rank(id) >= SkillTree.MaxRank) return false;
            return !SkillTree.All[(int)id].TryGetRequirement(out var required) || Rank(required) > 0;
        }

        public bool TryBuy(SkillId id)
        {
            if (!CanBuy(id)) return false;
            _ranks[(int)id]++;
            Points--;
            return true;
        }

        /// <summary>All ranks in one int, 2 bits per skill (for replication).</summary>
        public int PackedRanks
        {
            get
            {
                int packed = 0;
                for (int i = 0; i < SkillTree.Count; i++) packed |= _ranks[i] << (2 * i);
                return packed;
            }
        }

        public static int RankIn(int packedRanks, SkillId id) => (packedRanks >> (2 * (int)id)) & 3;

        /// <summary><see cref="CanBuy"/> from replicated numbers (for the client's skill tree).</summary>
        public static bool CanBuy(int packedRanks, int points, SkillId id)
        {
            if ((int)id < 0 || (int)id >= SkillTree.Count || points <= 0 || RankIn(packedRanks, id) >= SkillTree.MaxRank) return false;
            return !SkillTree.All[(int)id].TryGetRequirement(out var required) || RankIn(packedRanks, required) > 0;
        }

        public float FireRateMultiplier => FireRateFor(PackedRanks);
        public float RangeMultiplier => RangeFor(PackedRanks);
        public float RegenPerSecond => Rank(SkillId.SecondWind) * SkillTree.RegenPerRank;

        public static float FireRateFor(int packed) => 1f + RankIn(packed, SkillId.RapidFire) * SkillTree.FireRatePerRank;
        public static float RangeFor(int packed) => 1f + RankIn(packed, SkillId.LongBarrel) * SkillTree.RangePerRank;

        /// <summary>Puts the skill bonuses on a player's combat stats.</summary>
        public void ApplyTo(PlayerVitals vitals, float baseMaxHealth)
        {
            vitals.SkillDamageBonus = Rank(SkillId.Sharpshooter) * SkillTree.DamagePerRank;
            vitals.GivePotency = 1f + Rank(SkillId.Potency) * SkillTree.PotencyPerRank;
            vitals.GiveDuration = 1f + Rank(SkillId.Lingering) * SkillTree.DurationPerRank;
            vitals.SkillMoveBonus = Rank(SkillId.FleetFoot) * SkillTree.MovePerRank;
            vitals.SkillJumpBonus = Rank(SkillId.Spring) * SkillTree.JumpPerRank;
            vitals.SetMaxHealth(baseMaxHealth + Rank(SkillId.Toughness) * SkillTree.HealthPerRank);
        }
    }
}
