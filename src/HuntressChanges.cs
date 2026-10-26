using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EntityStates.Huntress;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;

namespace TaylorsBalancePatch;

internal sealed class HuntressChanges : IDisposable
{
    private sealed class ExtendedMarker { }

    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private readonly ConditionalWeakTable<ProjectileDotZone, ExtendedMarker> extendedZones =
        new ConditionalWeakTable<ProjectileDotZone, ExtendedMarker>();
    private SkillDef? blink;
    private float originalBlinkCooldown;
    private GameObject? arrowRainPrefab;
    private ProjectileDotZone? arrowRainZone;
    private float originalDamage;
    private bool damageChanged;

    internal HuntressChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Huntress", "Utility")) Add(() => On.RoR2.Skills.SkillCatalog.SetSkillDefs += SkillDefs,
                () => On.RoR2.Skills.SkillCatalog.SetSkillDefs -= SkillDefs);
            if (ChangeGroups.Enabled("Huntress", "Special")) Add(() => On.RoR2.ProjectileCatalog.SetProjectilePrefabs += Projectiles,
                () => On.RoR2.ProjectileCatalog.SetProjectilePrefabs -= Projectiles);
            if (ChangeGroups.Enabled("Huntress", "Special")) Add(() => On.RoR2.Projectile.ProjectileDotZone.Start += ExtendArrowRain,
                () => On.RoR2.Projectile.ProjectileDotZone.Start -= ExtendArrowRain);
            if (ChangeGroups.Enabled("Huntress", "Special")) Add(() => RoR2Application.onLoad += ApplyArrowRainDamage,
                () => RoR2Application.onLoad -= ApplyArrowRainDamage);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Huntress changes ENABLED: Blink cooldown 6s; Arrow Rain +5% damage and 100m upward hit volume with matching falling arrows. Native proc coefficient retained.");
        }
        catch (Exception error) { Fail(error); }
    }

    private void Add(Action install, Action remove)
    {
        cleanup.Add(remove);
        install();
    }

    private void Fail(Exception error)
    {
        Dispose();
        log.LogError($"Huntress setup failed; restored its values and removed its hooks. {error}");
    }

    public void Dispose()
    {
        if (blink != null) blink.baseRechargeInterval = originalBlinkCooldown;
        if (damageChanged) ArrowRain.damageCoefficient = originalDamage;
        blink = null;
        arrowRainPrefab = null;
        arrowRainZone = null;
        damageChanged = false;
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); }
            catch (Exception error) { log.LogError($"Huntress hook cleanup failed: {error}"); }
        cleanup.Clear();
    }

    private void SkillDefs(On.RoR2.Skills.SkillCatalog.orig_SetSkillDefs orig, SkillDef[] skillDefs)
    {
        try
        {
            if (blink == null)
            {
                blink = skillDefs.Single(s => s.skillNameToken == "HUNTRESS_UTILITY_NAME");
                originalBlinkCooldown = blink.baseRechargeInterval;
                blink.baseRechargeInterval = HuntressBalance.BlinkCooldown;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(skillDefs);
    }

    private void Projectiles(On.RoR2.ProjectileCatalog.orig_SetProjectilePrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (arrowRainZone == null)
            {
                var prefab = ArrowRain.projectilePrefab;
                if (!prefab || !prefabs.Contains(prefab))
                    prefab = prefabs.Single(p => p.name.IndexOf("ArrowRain", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        p.GetComponent<ProjectileDotZone>());
                var zone = prefab.GetComponent<ProjectileDotZone>();
                var controller = prefab.GetComponent<ProjectileController>();
                if (!zone || !controller)
                    throw new InvalidOperationException("Arrow Rain's projectile layout changed.");
                arrowRainPrefab = prefab;
                arrowRainZone = zone;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private void ApplyArrowRainDamage()
    {
        try
        {
            if (!damageChanged)
            {
                if (ArrowRain.damageCoefficient <= 0f || blink == null || arrowRainZone == null)
                    throw new InvalidOperationException("Huntress catalog setup was incomplete.");
                originalDamage = ArrowRain.damageCoefficient;
                ArrowRain.damageCoefficient *= HuntressBalance.ArrowRainDamageMultiplier;
                damageChanged = true;
                log.LogInfo($"Huntress catalog setup ready: Arrow Rain damage {originalDamage:0.###} -> {ArrowRain.damageCoefficient:0.###}.");
            }
        }
        catch (Exception error) { Fail(error); }
    }

    private void ExtendArrowRain(On.RoR2.Projectile.ProjectileDotZone.orig_Start orig, ProjectileDotZone self)
    {
        try
        {
            if (arrowRainPrefab && self && self.gameObject.name.StartsWith(arrowRainPrefab!.name, StringComparison.Ordinal) &&
                !extendedZones.TryGetValue(self, out _))
            {
                var group = self.GetComponent<HitBoxGroup>();
                if (!group || group.hitBoxes == null || group.hitBoxes.Length == 0)
                    throw new InvalidOperationException("Arrow Rain's hitbox group was not found.");

                var native = group.hitBoxes[0].transform;
                var size = native.lossyScale;
                // OverlapAttack reads transform scale, not Collider dimensions.
                // Preserve the native X/Z footprint and ground coverage.
                float bottom = Mathf.Min(self.transform.position.y, native.position.y - Mathf.Abs(size.y) * 0.5f);
                float top = self.transform.position.y + HuntressBalance.ArrowRainVerticalReach;
                var volume = new GameObject("TaylorArrowRainVerticalHitbox");
                volume.layer = native.gameObject.layer;
                volume.transform.position = new Vector3(native.position.x, (bottom + top) * 0.5f, native.position.z);
                volume.transform.rotation = Quaternion.identity;
                volume.transform.localScale = new Vector3(Mathf.Abs(size.x), top - bottom, Mathf.Abs(size.z));
                volume.transform.SetParent(self.transform, true);
                var hitBox = volume.AddComponent<HitBox>();
                hitBox.showAsSphere = false;
                group.hitBoxes = new[] { hitBox };
                self.isSphereOverlap = false;
                extendedZones.Add(self, new ExtendedMarker());
                ExtendArrowRainVisuals(self, volume.transform.position, volume.transform.lossyScale);
            }
        }
        catch (Exception error)
        {
            log.LogError($"Could not extend an Arrow Rain hit volume; that instance remains vanilla. {error}");
        }
        orig(self);
    }

    private void ExtendArrowRainVisuals(ProjectileDotZone zone, Vector3 center, Vector3 size)
    {
        try
        {
            foreach (var particles in zone.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particles.name != "ArrowsFalling") continue;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.transform.position = center;
                particles.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var scale = particles.transform.lossyScale;
                var main = particles.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Shape;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                main.maxParticles = Math.Max(main.maxParticles, 4096);
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                // Local Z points down. Distribute arrows through the whole column
                // immediately, retaining their native speed, size and lifetime.
                shape.scale = new Vector3(size.x / Mathf.Abs(scale.x), size.z / Mathf.Abs(scale.y),
                    size.y / Mathf.Abs(scale.z));
                var emission = particles.emission;
                emission.rateOverTimeMultiplier *= size.y / Mathf.Max(1f, ArrowRain.arrowRainRadius);
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                if (renderer) renderer.localBounds = new Bounds(Vector3.zero, shape.scale + Vector3.one * 4f);
                particles.Play(true);
            }
        }
        catch (Exception error) { log.LogError($"Could not extend Arrow Rain visuals: {error}"); }
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Huntress", token) ? HuntressBalance.GetDescription(token) : null) ?? orig(self, token);
}
