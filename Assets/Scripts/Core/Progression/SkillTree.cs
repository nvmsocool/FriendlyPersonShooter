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

    /// <summary>
    /// The skill tree's shape and numbers. Levels get about +20% of the game's +40% power budget
    /// (the rest is gear): a maxed branch is roughly +20% in its area. Max level gives
    /// <see cref="MaxPoints"/> points for <see cref="TotalRanks"/> ranks, so players choose.
    /// </summary>
    public static class SkillTree
    {
        public const int Count = 9;
        public const int PerBranch = 3;
        public const int MaxRank = 3;
        public const int TotalRanks = Count * MaxRank;
        public const int MaxLevel = 16;
        /// <summary>One point per level after the first.</summary>
        public const int MaxPoints = MaxLevel - 1;
        /// <summary>Every level costs the same XP, so levels come at a steady pace all session.</summary>
        public const int XpPerLevel = 300;

        public const float DamagePerRank = 0.04f;
        public const float FireRatePerRank = 0.03f;
        public const float RangePerRank = 0.10f;
        public const float PotencyPerRank = 0.07f;
        public const float DurationPerRank = 0.10f;
        /// <summary>Health per second per rank of Second Wind, after <see cref="RegenDelay"/> seconds unhurt.</summary>
        public const float RegenPerRank = 1f;
        public const float RegenDelay = 4f;
        public const float HealthPerRank = 7f;
        public const float MovePerRank = 0.03f;
        public const float JumpPerRank = 0.07f;

        private static string Pct(float f) => $"+{f * 100f:0}%";

        public static readonly SkillInfo[] All =
        {
            new SkillInfo(SkillId.Sharpshooter, SkillBranch.Offense, "Sharpshooter", Pct(DamagePerRank) + " damage"),
            new SkillInfo(SkillId.RapidFire, SkillBranch.Offense, "Rapid Fire", Pct(FireRatePerRank) + " fire rate"),
            new SkillInfo(SkillId.LongBarrel, SkillBranch.Offense, "Long Barrel", Pct(RangePerRank) + " range"),
            new SkillInfo(SkillId.Potency, SkillBranch.Support, "Potency", Pct(PotencyPerRank) + " heal and buff strength"),
            new SkillInfo(SkillId.Lingering, SkillBranch.Support, "Lingering", Pct(DurationPerRank) + " buff duration"),
            new SkillInfo(SkillId.SecondWind, SkillBranch.Support, "Second Wind", $"+{RegenPerRank:0.#} health/s after {RegenDelay:0} s unhurt"),
            new SkillInfo(SkillId.Toughness, SkillBranch.Survival, "Toughness", $"+{HealthPerRank:0} max health"),
            new SkillInfo(SkillId.FleetFoot, SkillBranch.Survival, "Fleet Foot", Pct(MovePerRank) + " move speed"),
            new SkillInfo(SkillId.Spring, SkillBranch.Survival, "Spring", Pct(JumpPerRank) + " jump"),
        };

        /// <summary>XP from the current level to the next. Flat, so the 15th level comes as fast as the 2nd.</summary>
        public static int XpToNext(int level) => XpPerLevel;
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
