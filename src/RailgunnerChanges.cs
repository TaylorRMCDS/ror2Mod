using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EntityStates.Railgunner.Weapon;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Projectile;
using UnityEngine;

namespace TaylorsBalancePatch;

internal sealed class RailgunnerChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new List<Action>();
    private readonly Dictionary<GameObject, GameObject> soundEmitters = new Dictionary<GameObject, GameObject>();
    private readonly HashSet<BuffWard> polarFields = new HashSet<BuffWard>();
    private bool projectilesChanged;
    private bool audioWarning;

    internal RailgunnerChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Railgunner", "Secondary")) Add(() => On.EntityStates.Railgunner.Weapon.BaseFireSnipe.OnFireBulletAuthority += StationarySecondary,
                () => On.EntityStates.Railgunner.Weapon.BaseFireSnipe.OnFireBulletAuthority -= StationarySecondary);
            if (ChangeGroups.Enabled("Railgunner", "Special")) cleanup.Add(SharedFreezeChanges.Acquire().Dispose);
            if (ChangeGroups.Enabled("Railgunner", "Utility")) Add(() => On.RoR2.BuffWard.OnEnable += FieldEnabled,
                () => On.RoR2.BuffWard.OnEnable -= FieldEnabled);
            if (ChangeGroups.Enabled("Railgunner", "Utility")) Add(() => On.RoR2.BuffWard.OnDisable += FieldDisabled,
                () => On.RoR2.BuffWard.OnDisable -= FieldDisabled);
            if (ChangeGroups.Enabled("Railgunner", "Utility")) Add(() => IL.RoR2.GlobalEventManager.OnCharacterHitGroundServer += PolarLanding,
                () => IL.RoR2.GlobalEventManager.OnCharacterHitGroundServer -= PolarLanding);
            if (ChangeGroups.Enabled("Railgunner", "Utility")) Add(() => On.EntityStates.GenericCharacterMain.ApplyJumpVelocity += PolarJump,
                () => On.EntityStates.GenericCharacterMain.ApplyJumpVelocity -= PolarJump);
            if (ChangeGroups.Enabled("Railgunner", "Passive")) Add(() => IL.RoR2.UI.SniperTargetViewer.OnTargetDiscovered += WeakPointVisual,
                () => IL.RoR2.UI.SniperTargetViewer.OnTargetDiscovered -= WeakPointVisual);
            if (ChangeGroups.Enabled("Railgunner", "Passive")) Add(() => IL.RoR2.BulletAttack.IsSniperTargetHit += WeakPointHit,
                () => IL.RoR2.BulletAttack.IsSniperTargetHit -= WeakPointHit);
            if (ChangeGroups.Enabled("Railgunner", "Special")) Add(() => On.EntityStates.Railgunner.Weapon.FireSnipeCryo.ModifyBullet += Cryo,
                () => On.EntityStates.Railgunner.Weapon.FireSnipeCryo.ModifyBullet -= Cryo);
            if (ChangeGroups.Enabled("Railgunner", "Utility")) Add(() => On.EntityStates.Railgunner.Weapon.FireMineBlinding.ModifyProjectileInfo += Polar,
                () => On.EntityStates.Railgunner.Weapon.FireMineBlinding.ModifyProjectileInfo -= Polar);
            if (ChangeGroups.Enabled("Railgunner", "Utility")) Add(() => On.RoR2.ProjectileCatalog.SetProjectilePrefabs += Projectiles,
                () => On.RoR2.ProjectileCatalog.SetProjectilePrefabs -= Projectiles);
            if (ChangeGroups.Enabled("Railgunner", "Secondary")) Add(() => On.RoR2.Util.PlaySound_string_GameObject += SecondarySound,
                () => On.RoR2.Util.PlaySound_string_GameObject -= SecondarySound);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Railgunner changes ENABLED: 70% weak-point size; secondary firing volume normal for shooter/50% for other players; Polar Field 15m, native damage/slow, 1500 outward force, 0.1s post-stick fuse, allied landing protection and 50% higher jumps; Cryocharge radius +20% and shared freeze thresholds.");
        }
        catch (Exception error) { Fail(error); }
    }

    private void Add(Action install, Action remove) { cleanup.Add(remove); install(); }

    private void Fail(Exception error)
    {
        Dispose();
        log.LogError($"Railgunner setup failed; restored original values and removed its hooks. {error}");
    }

    public void Dispose()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
        foreach (var emitter in soundEmitters.Values) if (emitter) UnityEngine.Object.Destroy(emitter);
        soundEmitters.Clear();
        polarFields.Clear();
        projectilesChanged = false;
    }

    private static void WeakPointVisual(ILContext il) => ScaleWeakPoint(il, false);
    private static void WeakPointHit(ILContext il) => ScaleWeakPoint(il, true);
    private static void ScaleWeakPoint(ILContext il, bool squared)
    {
        var cursor = new ILCursor(il);
        cursor.Goto(RailgunnerBalance.WeakPointSite(il.Body, squared), MoveType.After);
        cursor.Emit(OpCodes.Ldc_R4, squared ? RailgunnerBalance.WeakPointScale * RailgunnerBalance.WeakPointScale : RailgunnerBalance.WeakPointScale);
        cursor.Emit(OpCodes.Mul);
    }

    private static void Cryo(On.EntityStates.Railgunner.Weapon.FireSnipeCryo.orig_ModifyBullet orig,
        FireSnipeCryo self, BulletAttack bullet)
    {
        orig(self, bullet);
        bullet.radius *= RailgunnerBalance.CryoRadiusMultiplier;
    }

    private static void StationarySecondary(
        On.EntityStates.Railgunner.Weapon.BaseFireSnipe.orig_OnFireBulletAuthority orig,
        BaseFireSnipe self, Ray ray)
    {
        var body = self.outer ? self.outer.GetComponent<CharacterBody>() : null;
        var motor = body ? body.characterMotor : null;
        // The small self-push otherwise resets CharacterBody.UpdateNotMoving.
        // Keep native recoil in the air, during movement, and for special shots.
        if ((self is FireSnipeHeavy || self is FireSnipeLight) && motor && motor!.isGrounded &&
            body!.inputBank && body.inputBank.moveVector.sqrMagnitude == 0f &&
            motor.velocity.sqrMagnitude <= 0.0001f)
        {
            float force = self.selfKnockbackForce;
            self.selfKnockbackForce = 0f;
            try { orig(self, ray); }
            finally { self.selfKnockbackForce = force; }
            return;
        }
        orig(self, ray);
    }

    private static void Polar(On.EntityStates.Railgunner.Weapon.FireMineBlinding.orig_ModifyProjectileInfo orig,
        FireMineBlinding self, ref FireProjectileInfo info)
    {
        orig(self, ref info);
        // Native Polar damage is zero. Force is independently passed through
        // ProjectileExplosion into BlastAttack even when damage is zero.
        info.damage = RailgunnerBalance.PolarDamage;
        info.force = RailgunnerBalance.PolarForce;
    }

    private void Projectiles(On.RoR2.ProjectileCatalog.orig_SetProjectilePrefabs orig, GameObject[] prefabs)
    {
        try
        {
            if (!projectilesChanged)
            {
                var mine = prefabs.Single(p => p.name == "RailgunnerMineAlt");
                var field = prefabs.Single(p => p.name == "RailgunnerMineAltDetonated");
                var blast = mine.GetComponent<ProjectileExplosion>();
                var controller = mine.GetComponent<ProjectileController>();
                var ward = field.GetComponent<BuffWard>();
                var sphere = field.GetComponent<SphereCollider>();
                var slow = field.GetComponent<SlowDownProjectiles>();
                var fuse = mine.GetComponent<ProjectileFuse>();
                if (!blast || !controller || !ward || !sphere || !slow || !fuse || blast.childrenProjectilePrefab != field)
                    throw new InvalidOperationException("Polar Field's native explosion/child-field layout changed.");
                float oldRadius = blast.blastRadius, oldWardRadius = ward.radius, oldSphereRadius = sphere.radius, oldFuse = fuse.fuse;
                var oldFiltering = blast.blastAttackerFiltering;
                cleanup.Add(() =>
                {
                    if (blast) { blast.blastRadius = oldRadius; blast.blastAttackerFiltering = oldFiltering; }
                    if (ward) ward.radius = oldWardRadius;
                    if (sphere) sphere.radius = oldSphereRadius;
                    if (fuse) fuse.fuse = oldFuse;
                });
                blast.blastRadius = ward.radius = sphere.radius = RailgunnerBalance.PolarRadius;
                // Preserve the native blast multiplier (1), proc (0), child damage
                // (0), projectile slowdown (0.1), and enemy-slow BuffWard.
                blast.blastAttackerFiltering = AttackerFiltering.NeverHitSelf;
                fuse.fuse = oldFuse * RailgunnerBalance.DeploymentDelayMultiplier;
                projectilesChanged = true;
            }
        }
        catch (Exception error) { Fail(error); }
        orig(prefabs);
    }

    private void FieldEnabled(On.RoR2.BuffWard.orig_OnEnable orig, BuffWard self)
    {
        orig(self);
        if (self.gameObject.name == "RailgunnerMineAltDetonated(Clone)") polarFields.Add(self);
    }

    private void FieldDisabled(On.RoR2.BuffWard.orig_OnDisable orig, BuffWard self)
    {
        polarFields.Remove(self);
        orig(self);
    }

    private void PolarLanding(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.Goto(RailgunnerBalance.LandingImmunitySite(il.Body), MoveType.After);
        cursor.Emit(OpCodes.Ldarg_1);
        cursor.Emit(OpCodes.Ldarg_2);
        cursor.EmitDelegate<Func<bool, CharacterBody, CharacterMotor.HitGroundInfo, bool>>(
            (native, body, hit) => native || IsProtectedLanding(body, hit.position));
    }

    private void PolarJump(On.EntityStates.GenericCharacterMain.orig_ApplyJumpVelocity orig,
        CharacterMotor motor, CharacterBody body, float horizontal, float vertical, bool vault)
    {
        if (!vault && body && IsProtectedLanding(body, body.footPosition))
            vertical *= RailgunnerBalance.PolarJumpVelocityMultiplier;
        orig(motor, body, horizontal, vertical, vault);
    }

    private bool IsProtectedLanding(CharacterBody body, Vector3 position)
    {
        if (!body || !body.teamComponent) return false;
        foreach (var field in polarFields)
        {
            if (!field || !field.isActiveAndEnabled) continue;
            var team = field.GetComponent<TeamFilter>();
            var sphere = field.GetComponent<SphereCollider>();
            if (!team || !sphere || !sphere.enabled) continue;
            var scale = sphere.transform.lossyScale;
            float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            var center = sphere.transform.TransformPoint(sphere.center);
            if (RailgunnerBalance.InsidePolar((position - center).sqrMagnitude, radius,
                team.teamIndex == body.teamComponent.teamIndex, true)) return true;
        }
        return false;
    }

    private static float ListenerGain(AkAudioListener listener, CharacterBody shooter)
    {
        foreach (var local in LocalUserManager.readOnlyLocalUsersList)
        {
            var rig = local.cameraRigController;
            if (rig && (listener.transform == rig.transform || listener.transform.IsChildOf(rig.transform)))
                return RailgunnerBalance.ListenerVolume(local.cachedBody == shooter, true);
        }
        // Unknown/spectator listener ownership must never quiet the shooter's shot.
        return RailgunnerBalance.ListenerVolume(false, false);
    }

    private uint SecondarySound(On.RoR2.Util.orig_PlaySound_string_GameObject orig, string sound, GameObject source)
    {
        if (!source || !RailgunnerBalance.IsSecondarySound(sound)) return orig(sound, source);
        var body = source.GetComponent<CharacterBody>();
        if (!body || body.baseNameToken != "RAILGUNNER_BODY_NAME") return orig(sound, source);
        try
        {
            var listeners = AkAudioListener.DefaultListeners.ListenerList;
            if (listeners.Count == 0) return orig(sound, source);
            if (!soundEmitters.TryGetValue(source, out var emitter) || !emitter)
            {
                emitter = new GameObject("TaylorRailgunnerSecondaryAudio");
                emitter.transform.SetParent(source.transform, false);
                soundEmitters[source] = emitter;
                emitter.AddComponent<AkGameObj>();
            }
            foreach (var listener in listeners)
                if (listener && AkSoundEngine.SetGameObjectOutputBusVolume(emitter, listener.gameObject,
                    ListenerGain(listener, body)) != AKRESULT.AK_Success)
                    throw new InvalidOperationException("Wwise did not accept the secondary-only output gain.");
            return orig(sound, emitter);
        }
        catch (Exception error)
        {
            if (!audioWarning) { audioWarning = true; log.LogWarning($"Railgunner secondary audio reduction unavailable; using original audio. {error}"); }
            return orig(sound, source);
        }
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Railgunner", token) ? RailgunnerBalance.GetDescription(token) : null) ?? orig(self, token);
}
