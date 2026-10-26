using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using EntityStates.DroneTech.Weapon;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using RoR2.Networking;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class OperatorChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new();
    private readonly List<NetworkClient> registeredClients = new();
    private ConditionalWeakTable<CharacterBody, BodyUses> bodies = new();
    private ConditionalWeakTable<ShieldFormation, Cast> formations = new();
    private ConditionalWeakTable<GameObject, Cast> shields = new();
    private ConditionalWeakTable<GameObject, Cast> projectiles = new();
    private readonly List<Cast> pending = new();

    private sealed class BodyUses { public BodyUses() { } internal uint sequence; internal Cast? latest; }
    private sealed class Cast
    {
        internal CharacterBody body = null!;
        internal GenericSkill skill = null!;
        internal SkillDef definition = null!;
        internal uint sequence;
        internal readonly FirewallUse outcome = new();
        internal readonly List<GameObject> projectiles = new();
        internal bool credited;
        internal int endFrame;
    }

    internal OperatorChanges(ManualLogSource log) => this.log = log;
    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => On.EntityStates.DroneTech.Weapon.ShieldFormation.OnEnter += Enter,
                () => On.EntityStates.DroneTech.Weapon.ShieldFormation.OnEnter -= Enter);
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => On.EntityStates.DroneTech.Weapon.ShieldFormation.OnExit += Exit,
                () => On.EntityStates.DroneTech.Weapon.ShieldFormation.OnExit -= Exit);
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => On.EntityStates.DroneTech.Weapon.ShieldFormation.ServerShieldReceived += ShieldReceived,
                () => On.EntityStates.DroneTech.Weapon.ShieldFormation.ServerShieldReceived -= ShieldReceived);
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => On.EntityStates.DroneTech.Weapon.FireShield.OnEnter += Release,
                () => On.EntityStates.DroneTech.Weapon.FireShield.OnEnter -= Release);
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => On.DroneTechShieldController.OnIncomingDamageServer += BlockedDamage,
                () => On.DroneTechShieldController.OnIncomingDamageServer -= BlockedDamage);
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => On.RoR2.Projectile.ProjectileManager.FireProjectileImmediateServer += SpawnProjectile,
                () => On.RoR2.Projectile.ProjectileManager.FireProjectileImmediateServer -= SpawnProjectile);
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => IL.RoR2.Projectile.DroneTechShieldProjectile.OnTriggerEnter += ProjectileBlock,
                () => IL.RoR2.Projectile.DroneTechShieldProjectile.OnTriggerEnter -= ProjectileBlock);
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => GlobalEventManager.onServerDamageDealt += Damage,
                () => GlobalEventManager.onServerDamageDealt -= Damage);
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => RoR2Application.onFixedUpdate += Resolve,
                () => RoR2Application.onFixedUpdate -= Resolve);
            if (ChangeGroups.Enabled("Operator", "Utility")) Add(() => NetworkManagerSystem.onStartClientGlobal += RegisterClient,
                () => NetworkManagerSystem.onStartClientGlobal -= RegisterClient);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            foreach (var client in NetworkClient.allClients) RegisterClient(client);
            log.LogInfo("Operator changes ENABLED: FIREWALL refunds half its final cooldown only after blocking nothing and dealing no damage across its held shield and launched projectile.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Operator setup failed; removed hooks. {error}");
        }
    }

    private void Add(Action install, Action remove) { install(); cleanup.Add(remove); }
    public void Dispose()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
        foreach (var client in registeredClients)
            if (client.handlers.TryGetValue(OperatorBalance.RefundMessageId, out var handler) && handler == ReceiveRefund)
                client.UnregisterHandler(OperatorBalance.RefundMessageId);
        registeredClients.Clear();
        pending.Clear();
        bodies = new(); formations = new(); shields = new(); projectiles = new();
    }

    private void Enter(On.EntityStates.DroneTech.Weapon.ShieldFormation.orig_OnEnter orig, ShieldFormation self)
    {
        var body = self.outer ? self.outer.GetComponent<CharacterBody>() : null;
        var skill = self.activatorSkillSlot ? self.activatorSkillSlot : body?.skillLocator?.utility;
        if (body && skill && skill!.skillDef && skill.skillDef.skillNameToken == OperatorBalance.SkillNameToken)
        {
            var uses = bodies.GetOrCreateValue(body!);
            var cast = new Cast { body = body!, skill = skill, definition = skill.skillDef, sequence = ++uses.sequence };
            uses.latest = cast;
            formations.Remove(self); // State instances can be pooled.
            formations.Add(self, cast);
            if (NetworkServer.active) pending.Add(cast);
        }
        orig(self);
    }

    private void ShieldReceived(On.EntityStates.DroneTech.Weapon.ShieldFormation.orig_ServerShieldReceived orig,
        ShieldFormation self, GameObject shield)
    {
        if (NetworkServer.active && shield && formations.TryGetValue(self, out var cast))
        {
            shields.Remove(shield);
            shields.Add(shield, cast);
        }
        orig(self, shield);
    }

    private void Exit(On.EntityStates.DroneTech.Weapon.ShieldFormation.orig_OnExit orig, ShieldFormation self)
    {
        orig(self);
        if (formations.TryGetValue(self, out var cast))
        {
            cast.outcome.HoldingEnded = true;
            cast.endFrame = Time.frameCount;
        }
        formations.Remove(self);
    }

    private void Release(On.EntityStates.DroneTech.Weapon.FireShield.orig_OnEnter orig, FireShield self)
    {
        var body = self.outer ? self.outer.GetComponent<CharacterBody>() : null;
        if (body && bodies.TryGetValue(body!, out var uses) && uses.latest != null)
            uses.latest.outcome.ExpectsProjectile = true;
        orig(self);
    }

    private void BlockedDamage(On.DroneTechShieldController.orig_OnIncomingDamageServer orig,
        DroneTechShieldController self, DamageInfo info)
    {
        // Record before native rejection mutates damage and can kill the shield drone.
        if (NetworkServer.active && info.damage > 0f && !info.rejected && shields.TryGetValue(self.gameObject, out var cast))
            cast.outcome.Block();
        orig(self, info);
    }

    private GameObject SpawnProjectile(On.RoR2.Projectile.ProjectileManager.orig_FireProjectileImmediateServer orig,
        ProjectileManager self, FireProjectileInfo info, NetworkConnection connection, ushort predictionId, double fastForwardTime)
    {
        var projectile = orig(self, info, connection, predictionId, fastForwardTime);
        if (NetworkServer.active && projectile && info.projectilePrefab &&
            OperatorBalance.IsFirewallProjectile(info.projectilePrefab.name) && info.owner)
        {
            var body = info.owner.GetComponent<CharacterBody>();
            if (body && bodies.TryGetValue(body, out var uses) && uses.latest != null)
            {
                var cast = uses.latest;
                cast.outcome.ExpectsProjectile = cast.outcome.SawProjectile = true;
                cast.projectiles.Add(projectile);
                projectiles.Remove(projectile);
                projectiles.Add(projectile, cast);
            }
        }
        return projectile;
    }

    private void ProjectileBlock(ILContext il)
    {
        var cursor = new ILCursor(il);
        cursor.Goto(OperatorBalance.ProjectileBlockSite(il.Body), MoveType.Before);
        // The native branch has already verified an enemy ProjectileController and
        // is about to destroy it. Friendly pushes and unrelated triggers never count.
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Action<DroneTechShieldProjectile>>(shield =>
        {
            var projectile = shield.GetComponentInParent<ProjectileController>();
            if (NetworkServer.active && projectile && projectiles.TryGetValue(projectile.gameObject, out var cast))
                cast.outcome.Block();
        });
    }

    private void Damage(DamageReport report)
    {
        var info = report.damageInfo;
        if (NetworkServer.active && info.inflictor && !info.rejected &&
            projectiles.TryGetValue(info.inflictor, out var cast) && report.attackerBody == cast.body)
            cast.outcome.Damage(report.damageDealt);
    }

    private void Resolve()
    {
        if (!NetworkServer.active) return;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var cast = pending[i];
            if (!Current(cast)) { pending.RemoveAt(i); continue; }
            // Let FireShield.OnEnter run after ShieldFormation.OnExit, and let all
            // damage callbacks finish before checking destroyed projectile objects.
            if (!cast.outcome.HoldingEnded || Time.frameCount <= cast.endFrame) continue;
            bool alive = cast.projectiles.Exists(p => p);
            if (!cast.outcome.TryComplete(alive, out bool refund)) continue;
            pending.RemoveAt(i);
            if (!refund) continue;
            var identity = cast.body.GetComponent<NetworkIdentity>();
            if (identity && Util.HasEffectiveAuthority(identity)) ApplyRefund(cast);
            else if (identity && identity.clientAuthorityOwner != null)
                identity.clientAuthorityOwner.Send(OperatorBalance.RefundMessageId,
                    new RefundMessage { body = identity.netId, sequence = cast.sequence });
        }
    }

    private bool Current(Cast cast) => cast.body && cast.body.healthComponent && cast.body.healthComponent.alive &&
        cast.skill && cast.skill.skillDef == cast.definition &&
        bodies.TryGetValue(cast.body, out var uses) && ReferenceEquals(uses.latest, cast);

    private void ApplyRefund(Cast cast)
    {
        if (cast.credited || !Current(cast)) return;
        cast.credited = true;
        if (cast.skill.stock < cast.skill.maxStock)
            cast.skill.rechargeStopwatch += OperatorBalance.CooldownCredit(cast.skill.GetFinalRechargeInterval());
    }

    private void RegisterClient(NetworkClient client)
    {
        if (registeredClients.Contains(client)) return;
        if (client.handlers.ContainsKey(OperatorBalance.RefundMessageId))
        {
            log.LogWarning("Operator FIREWALL refund message ID is already registered; preserving that handler. Remote refunds are unavailable on this client.");
            return;
        }
        client.RegisterHandler(OperatorBalance.RefundMessageId, ReceiveRefund);
        registeredClients.Add(client);
    }

    private void ReceiveRefund(NetworkMessage message)
    {
        if (NetworkServer.active) return; // Host applies locally, once.
        var refund = message.ReadMessage<RefundMessage>();
        var owner = ClientScene.FindLocalObject(refund.body);
        var body = owner ? owner.GetComponent<CharacterBody>() : null;
        var identity = owner ? owner.GetComponent<NetworkIdentity>() : null;
        if (body && identity && Util.HasEffectiveAuthority(identity) && bodies.TryGetValue(body!, out var uses) &&
            uses.latest != null && uses.latest.sequence == refund.sequence)
            ApplyRefund(uses.latest);
    }

    private sealed class RefundMessage : MessageBase
    {
        public RefundMessage() { }
        internal NetworkInstanceId body;
        internal uint sequence;
        public override void Serialize(NetworkWriter writer) { writer.Write(body); writer.Write(sequence); }
        public override void Deserialize(NetworkReader reader) { body = reader.ReadNetworkId(); sequence = reader.ReadUInt32(); }
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Operator", token) ? OperatorBalance.GetDescription(token) : null) ?? orig(self, token);
}
