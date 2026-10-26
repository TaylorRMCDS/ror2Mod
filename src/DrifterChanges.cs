using System;
using System.Collections.Generic;
using BepInEx.Logging;
using EntityStates.Drifter;
using MonoMod.Cil;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal sealed class DrifterChanges : IDisposable
{
    private readonly ManualLogSource log;
    private readonly List<Action> cleanup = new();
    internal DrifterChanges(ManualLogSource log) => this.log = log;

    internal void Enable()
    {
        try
        {
            if (ChangeGroups.Enabled("Drifter", "Utility")) Add(() => IL.EntityStates.Drifter.TornadoSlam.OnEnter += SpinSteering,
                () => IL.EntityStates.Drifter.TornadoSlam.OnEnter -= SpinSteering);
            if (ChangeGroups.Enabled("Drifter", "Utility")) Add(() => IL.EntityStates.Drifter.TornadoSlam.UpdateDirection += SpinDirection,
                () => IL.EntityStates.Drifter.TornadoSlam.UpdateDirection -= SpinDirection);
            if (ChangeGroups.Enabled("Drifter", "Utility")) Add(() => IL.EntityStates.Drifter.ChargeTornadoSlam.OnEnter += ChargeSteering,
                () => IL.EntityStates.Drifter.ChargeTornadoSlam.OnEnter -= ChargeSteering);
            if (ChangeGroups.Enabled("Drifter", "Utility")) Add(() => On.JunkPickup.FixedUpdate += PullJunk,
                () => On.JunkPickup.FixedUpdate -= PullJunk);
            Add(() => On.RoR2.Language.GetLocalizedStringByToken += Description,
                () => On.RoR2.Language.GetLocalizedStringByToken -= Description);
            log.LogInfo("Drifter changes ENABLED: 3x Tornado Slam charge/spin steering; spinning pulls unclaimed allied Junk within 15m with line of sight. Native collisions, damage, speed, Junk generation and cancellation are preserved.");
        }
        catch (Exception error)
        {
            Dispose();
            log.LogError($"Drifter setup failed; removed its hooks. {error}");
        }
    }

    private void Add(Action install, Action remove) { install(); cleanup.Add(remove); }
    public void Dispose()
    {
        for (int i = cleanup.Count - 1; i >= 0; i--)
            try { cleanup[i](); } catch (Exception error) { log.LogError(error); }
        cleanup.Clear();
    }

    private static void SpinSteering(ILContext il) => DrifterBalance.PatchSteeringReads(il.Body, 1);
    private static void SpinDirection(ILContext il) => DrifterBalance.PatchSteeringReads(il.Body, 2);
    private static void ChargeSteering(ILContext il) => DrifterBalance.PatchSteeringReads(il.Body, 1);

    private static void PullJunk(On.JunkPickup.orig_FixedUpdate orig, JunkPickup self)
    {
        orig(self); // Native pickup delay and collection logic remain authoritative.
        if (!NetworkServer.active || !self || !self.isActiveAndEnabled) return;
        var gravitate = self.gravitatePickup;
        if (!gravitate || !gravitate.rigidbody || gravitate.rigidbody.isKinematic ||
            gravitate.gravitateTarget || !gravitate.teamFilter) return;
        var rigidbody = gravitate.rigidbody;
        var position = rigidbody.position;
        var target = FindPullTarget(position, gravitate.teamFilter.teamIndex);
        if (!target) return;
        var delta = target!.corePosition - position;
        var desired = delta.normalized * DrifterBalance.PullSpeed(delta.magnitude, Time.fixedDeltaTime);
        rigidbody.velocity = Vector3.MoveTowards(rigidbody.velocity, desired,
            DrifterBalance.JunkPullAcceleration * Time.fixedDeltaTime);
        // No persistent target or collector flags: the next tick rechecks active
        // spinning, range and line of sight; exiting stops this attraction at once.
    }

    private static CharacterBody? FindPullTarget(Vector3 position, TeamIndex team)
    {
        if (team == TeamIndex.None) return null;
        CharacterBody? nearest = null;
        float closest = DrifterBalance.JunkPullRadius * DrifterBalance.JunkPullRadius;
        foreach (var body in CharacterBody.readOnlyInstancesList)
        {
            if (!body || body.baseNameToken != "DRIFTER_BODY_NAME" || !body.healthComponent ||
                !body.healthComponent.alive || !body.teamComponent || body.teamComponent.teamIndex != team) continue;
            float distance = (body.corePosition - position).sqrMagnitude;
            if (!DrifterBalance.WithinPullRadius(distance) || distance > closest || !Spinning(body) ||
                Physics.Linecast(position, body.corePosition, LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
            nearest = body;
            closest = distance;
        }
        return nearest;
    }

    private static bool Spinning(CharacterBody body)
    {
        foreach (var machine in body.GetComponents<EntityStateMachine>())
            if (machine && machine.state is TornadoSlam) return true;
        return false;
    }

    private static string Description(On.RoR2.Language.orig_GetLocalizedStringByToken orig,
        Language self, string token) => (ChangeGroups.DescriptionEnabled("Drifter", token) ? DrifterBalance.GetDescription(token) : null) ?? orig(self, token);
}
