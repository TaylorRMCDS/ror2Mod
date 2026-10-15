using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace TaylorsBalancePatch;

internal static class RexDisperseVisual
{
    internal const float Lifetime = 0.20f;

    internal static void Spawn(GameObject sonicEffect, Vector3 origin)
    {
        // The skill state already runs on each observing client, just like its
        // original local muzzle effect. Do not transmit a second effect message.
        if (!NetworkClient.active) return;
        var material = sonicEffect.GetComponentsInChildren<ParticleSystemRenderer>(true)
            .SelectMany(r => r.sharedMaterials)
            .FirstOrDefault(m => m && m.name.StartsWith("matOmniRing1Generic", StringComparison.Ordinal));
        if (!material) throw new InvalidOperationException("REX's existing ring material was not found.");

        var ring = new GameObject("TaylorDisperseRangeRing");
        Mesh? quad = null;
        // Keep the ring at the cast point, slightly clear of flat ground.
        ring.transform.position = origin + Vector3.up * 0.15f;
        try
        {
            var particles = ring.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = Lifetime;
            main.startLifetime = Lifetime;
            main.startSpeed = 0f;
            main.startSize = RexBalance.DisperseRadius * 2f;
            main.startColor = new Color(0.72f, 0.95f, 0.65f, 0.8f);
            main.startRotation3D = true;
            main.startRotationX = Mathf.PI * 0.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1;
            main.gravityModifier = 0f;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;

            // Reach the boundary before fading, so it remains briefly readable.
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.04f, 0f, 1.28f),
                new Keyframe(0.75f, 1f, 1.28f, 0f),
                new Keyframe(1f, 1f, 0f, 0f)));
            var fade = particles.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            // A mesh avoids billboard screen-size clamping: the displayed
            // diameter must stay 26 world units even when the camera is close.
            quad = new Mesh
            {
                name = "TaylorDisperseRingQuad",
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 }
            };
            quad.RecalculateBounds();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = quad;
            renderer.alignment = ParticleSystemRenderSpace.World;
            // This material already supports particle vertex colors. Reuse it
            // unchanged; the green tint belongs only to our emitted particle.
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            particles.Play();
            particles.Emit(1);
            UnityEngine.Object.Destroy(ring, Lifetime + 0.1f);
            UnityEngine.Object.Destroy(quad, Lifetime + 0.1f);
        }
        catch
        {
            UnityEngine.Object.Destroy(ring);
            if (quad) UnityEngine.Object.Destroy(quad);
            throw;
        }
    }
}
