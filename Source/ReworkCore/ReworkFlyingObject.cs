using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Rework.Core;

/// <summary>
/// Objeto volador del knockback ([ReworkKnockback]).
///
/// Traslada un Thing (normalmente el pawn golpeado) desde su posición hasta el
/// destino calculado por ReworkKnockbackRuntime, dibujándolo por el aire, y al
/// llegar lo suelta con el daño de impacto. Si el destino se sale del mapa, lo
/// suelta en la última celda válida.
///
/// Equivale al FlyingObject de JecsTools, recortado a lo que el knockback
/// necesita: sin proyectiles, sin explosiones propias (la explosión del golpe
/// la hace el runtime antes de lanzar), sin equipables. La cuenta de vuelo es
/// ticks = distancia / velocidad, con mínimo 1.
///
/// El vuelo NO usa verbs ni jobs: el pawn vuelve a existir al aterrizar con
/// GenSpawn.Spawn, así que reservas y trabajos se reanudan solos.
/// </summary>
public class ReworkFlyingObject : ThingWithComps
{
    private Vector3 origin;
    private Vector3 destination;
    private int ticksToImpact;
    private float speed = 30f;
    private Thing? flyingThing;
    private Thing? launcher;
    private DamageInfo? impactDamage;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref origin, "origin");
        Scribe_Values.Look(ref destination, "destination");
        Scribe_Values.Look(ref ticksToImpact, "ticksToImpact");
        Scribe_Values.Look(ref speed, "speed", 30f);
        Scribe_References.Look(ref flyingThing, "flyingThing");
        Scribe_References.Look(ref launcher, "launcher");
    }

    /// <summary>Lanza un Thing ya des-spawneado hacia el destino.</summary>
    public void Launch(Thing launcher, Vector3 origin, IntVec3 destCell, Thing flyingThing, float speed, DamageInfo? impactDamage)
    {
        this.launcher = launcher;
        this.origin = origin;
        this.destination = destCell.ToVector3Shifted();
        this.flyingThing = flyingThing;
        this.speed = speed > 0f ? speed : 30f;
        this.impactDamage = impactDamage;

        float dist = (this.destination - this.origin).MagnitudeHorizontal();
        float ticks = dist / (this.speed * 0.06f);
        ticksToImpact = Math.Max(1, Mathf.CeilToInt(ticks));
    }

    private float StartTicks
    {
        get
        {
            float dist = (destination - origin).MagnitudeHorizontal();
            float ticks = dist / (speed * 0.06f);
            return Math.Max(0.001f, ticks);
        }
    }

    private Vector3 ExactPosition
    {
        get
        {
            float t = Mathf.Clamp01(1f - ticksToImpact / StartTicks);
            Vector3 flat = origin.Yto0() + (destination - origin).Yto0() * t;
            return flat + Vector3.up * def.Altitude;
        }
    }

    public override Vector3 DrawPos => ExactPosition;

    public override void Tick()
    {
        base.Tick();
        ticksToImpact--;
        Vector3 exact = ExactPosition;
        if (!exact.InBounds(Map))
        {
            Position = origin.ToIntVec3();
            Land();
        }
        else if (ticksToImpact <= 0)
        {
            IntVec3 dest = new IntVec3(destination);
            Position = dest.InBounds(Map) ? dest : exact.ToIntVec3();
            Land();
        }
        else
        {
            Position = exact.ToIntVec3();
        }
    }

    public override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        if (flyingThing == null)
            return;
        try
        {
            if (flyingThing is Pawn pawn)
            {
                pawn.Drawer.renderer.RenderPawnAt(DrawPos);
            }
            else
            {
                Graphics.DrawMesh(MeshPool.plane10, DrawPos, Quaternion.LookRotation((destination - origin).Yto0()),
                    flyingThing.def.DrawMatSingle, 0);
            }
            Comps_PostDraw();
        }
        catch
        {
            // Un dibujo roto no rompe el vuelo.
        }
    }

    private void Land()
    {
        try
        {
            var map = Map;
            var pos = Position;
            var thing = flyingThing;

            if (thing != null && map != null)
            {
                try { GenSpawn.Spawn(thing, pos, map, WipeMode.Vanish); }
                catch (Exception e)
                {
                    Lg.Error($"[ReworkKnockback] No se pudo soltar '{thing}' al aterrizar: {e.Message}");
                }

                if (impactDamage is DamageInfo dinfo && dinfo.Amount > 0f && thing != null && !thing.Destroyed)
                {
                    try { thing.TakeDamage(dinfo); }
                    catch (Exception e)
                    {
                        Lg.Error($"[ReworkKnockback] Daño de impacto falló: {e.Message}");
                    }
                }
            }
        }
        finally
        {
            Destroy();
        }
    }
}
