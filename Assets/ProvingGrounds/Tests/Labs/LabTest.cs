using NUnit.Framework;
using UnityEngine;
using ForgeAttribute = FarEmerald.PlayForge.Attribute;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FarEmerald.PlayForge.ProvingGrounds.Tests
{
    /// <summary>
    /// What the content labs share: assets read from a lab's folder, a clock whose frames each take an exact step, rolls
    /// the test writes, and entities made from a lab's identity assets on either hub. A lab's assets are as an author
    /// left them in the inspector; its tests play them as the game does.
    /// </summary>
    public abstract class LabTest : ProvingGroundsTest
    {
        /// <summary>The lab's folder, as Assets/ProvingGrounds/Hades.</summary>
        protected abstract string Root { get; }

        // A clock whose frames each take a fixed step
        private sealed class SteppingClock : IForgeClock
        {
            public float Step;
            public float DeltaTime => Step;
            public float UnscaledDeltaTime => Step;
            public float FixedDeltaTime => Step;
            public float Time => UnityEngine.Time.time;
            public float UnscaledTime => UnityEngine.Time.unscaledTime;
            public int FrameCount => UnityEngine.Time.frameCount;
        }

        /// <summary>Rolls that come out as the test says, one after another, and how many were drawn.</summary>
        protected sealed class ScriptedRandom : IForgeRandom
        {
            private readonly float[] rolls;

            public ScriptedRandom(params float[] rolls) => this.rolls = rolls;

            public int Drawn { get; private set; }
            public float Value => rolls[Drawn++ % rolls.Length];
            public Vector2 InsideUnitCircle => Vector2.zero;
            public Vector3 InsideUnitSphere => Vector3.zero;
        }

        private IForgeClock previousClock;
        private IForgeRandom previousRandom;
        private bool clockSet, randomSet;

        /// <summary>Each frame takes <paramref name="step"/> seconds until the test ends.</summary>
        protected void Clock(float step)
        {
            var previous = ForgeTime.Use(new SteppingClock { Step = step });
            if (!clockSet) previousClock = previous;
            clockSet = true;
        }

        /// <summary>Rolls come out as these, in turn, until the test ends.</summary>
        protected ScriptedRandom Rolls(params float[] rolls)
        {
            var scripted = new ScriptedRandom(rolls);
            var previous = ForgeRandom.Use(scripted);
            if (!randomSet) previousRandom = previous;
            randomSet = true;
            return scripted;
        }

        [TearDown]
        public void PutBackTimeAndChance()
        {
            if (clockSet) ForgeTime.Use(previousClock);
            if (randomSet) ForgeRandom.Use(previousRandom);
            clockSet = randomSet = false;
        }

        protected T Load<T>(string path) where T : Object
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<T>($"{Root}/{path}.asset");
            Assert.IsNotNull(asset, $"{path} is an asset");
            return asset;
#else
            Assert.Ignore("A lab's assets are read from the asset database, in the editor");
            return null;
#endif
        }

        protected static float Value(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.CurrentValue : float.NaN;

        protected static float Base(IGameplayAbilitySystem gas, ForgeAttribute attribute) =>
            gas.TryGetAttributeValue(attribute, out var value) ? value.BaseValue : float.NaN;

        protected static int Stacks(IGameplayAbilitySystem gas, GameplayEffect effect) =>
            gas.TryGetEffectContainer(effect, out var container) ? (container as AbstractStackingEffectContainer)?.Stacks ?? 1 : 0;

        protected static bool Has(IGameplayAbilitySystem gas, GameplayEffect effect) => gas.TryGetEffectContainer(effect, out _);

        protected static bool HasTag(IGameplayAbilitySystem gas, string tag) => gas.GetTagCache().GetWeight(Tag.Generate(tag)) > 0;

        /// <summary>An entity made from a lab's identity asset, registered on either hub.</summary>
        protected IGameplayAbilitySystem FromIdentity(Hub hub, EntityIdentity identity)
        {
            if (hub == Hub.Runtime)
            {
                var gas = new RuntimeGameplayAbilitySystem(identity);
                World.Register(gas);
                return gas;
            }

            // The world's Mono entity, its host the world's to clean up, with the lab's identity in place of its own
            var mono = World.NewMonoEntity(name: identity.name);
            mono.EntityData = identity;
            World.Register(mono);
            return mono;
        }

        /// <summary>
        /// An entity of these attributes, on a team, registered on either hub. Its team is its identity's: a hub can't
        /// change an entity's side once it's made.
        /// </summary>
        protected IGameplayAbilitySystem Entity(Hub hub, string name, string team, params AttributeSetElement[] elements)
        {
            var set = Make.Set(elements);
            var sides = new System.Collections.Generic.List<Tag>();
            if (!string.IsNullOrEmpty(team)) sides.Add(Tag.Generate(team));
            if (hub == Hub.Runtime)
            {
                var identity = Make.RuntimeIdentity(name, set);
                identity.Affiliation = sides;
                var gas = new RuntimeGameplayAbilitySystem(identity);
                World.Register(gas);
                return gas;
            }

            var mono = World.NewMonoEntity(set, name);
            mono.EntityData.Affiliation = sides;
            World.Register(mono);
            return mono;
        }

        /// <summary>An attribute's element: a pool, as health, stays between 0 and its base; a stat doesn't.</summary>
        protected AttributeSetElement Pool(ForgeAttribute attribute, float current, float @base) =>
            Make.Element(attribute, current, @base, EAttributeOverflowPolicy.ZeroToBase);

        protected AttributeSetElement Stat(ForgeAttribute attribute, float value) => Make.Element(attribute, value, value);

        /// <summary>Gives the caster the ability at a level, and asks it to activate at a target, if any.</summary>
        protected bool Cast(IGameplayAbilitySystem caster, Ability ability, IGameplayAbilitySystem target = null, int level = 1)
        {
            var abilities = caster.GetAbilitySystem();
            Assert.That(abilities.GiveAbility(ability, level, out int index), $"{ability.name} is given");
            return Cast(caster, index, target);
        }

        /// <summary>Asks the ability in a slot to activate at a target, if any.</summary>
        protected static bool Cast(IGameplayAbilitySystem caster, int index, IGameplayAbilitySystem target = null)
        {
            var abilities = caster.GetAbilitySystem();
            var request = ProcessDataPacket.Default();
            if (target != null) request.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            return abilities.TryActivateAbility(abilities.CreateActivationRequest(index, request));
        }

        /// <summary>Asks an ability the entity has already, as an item's, to activate at a target, if any.</summary>
        protected static bool Use(IGameplayAbilitySystem caster, Ability ability, IGameplayAbilitySystem target = null)
        {
            var data = ProcessDataPacket.Default();
            if (target != null) data.SetPrimary(Tags.TARGET_REAL, (ITarget)target);
            return caster.GetAbilitySystem().TryActivateAbility(ability, data);
        }

        /// <summary>Gives and equips an item, with as many stacks as asked.</summary>
        protected static void Hold(IGameplayAbilitySystem holder, Item item, int stacks = 1)
        {
            var items = holder.GetItemSystem();
            Assert.That(items.GiveAndEquipItem(item, out _), $"{item.name} is held");
            if (stacks > 1) Assert.That(items.GiveItem(item, stacks - 1), Is.EqualTo(stacks - 1), $"{stacks} {item.name}");
        }

        /// <summary>Applies an effect from a source to a target, as a hit.</summary>
        protected bool Hit(IGameplayAbilitySystem source, IGameplayAbilitySystem target, GameplayEffect effect) => World.Apply(source, target, effect);
    }
}
