"""Generate the Warcraft particle library (assets/addon/particles/warcraft/*.vpcf).

Every effect is built from a few archetypes (motes, body, burst, nova, column, line, ...)
recoloured per palette entry, so abilities pick "burst + holy" instead of authoring files.
Operators and field names follow Valve's own CS2 particles (decompiled for reference);
build.ps1 compiles the output with resourcecompiler.

    python assets/tools/make_particles.py
"""
import os
import shutil

ROOT = os.path.join(os.path.dirname(__file__), "..", "addon", "particles", "warcraft")
HEADER = ("<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} "
          "format:vpcf54:version{326b1595-45e8-4004-aa5a-3e08655ff51f} -->\n")

# name -> (main colour, highlight colour); keep in sync with FxColor in WarcraftParticles.cs
PALETTE = {
    "heal":   ((60, 255, 110), (200, 255, 190)),
    "fire":   ((255, 110, 20), (255, 220, 120)),
    "frost":  ((90, 190, 255), (220, 245, 255)),
    "holy":   ((255, 205, 70), (255, 250, 210)),
    "war":    ((255, 45, 30), (255, 170, 120)),
    "storm":  ((110, 160, 255), (230, 240, 255)),
    "blood":  ((190, 10, 25), (255, 90, 90)),
    "poison": ((120, 235, 30), (220, 255, 140)),
    "arcane": ((165, 90, 255), (235, 200, 255)),
    "shadow": ((70, 40, 120), (160, 120, 220)),
    "nature": ((80, 200, 60), (210, 255, 150)),
}

GLOW = "materials/particle/particle_glow_05.vtex"
SOFT = "materials/particle/particle_glow_01.vtex"
SMOKE = "materials/particle/smoke1/smoke1.vtex"
SNOW = "materials/particle/snow.vtex"
FIRE = "materials/particle/fire_burning_character/fire_burning_character.vtex"
SPARK = "materials/particle/sparks/sparks.vtex"
BOLT = "materials/particle/bendibeam.vtex"
ENERGY = "materials/particle/beam_energy_01.vtex"


# ---------------------------------------------------------------- KV3 writer
class Res(str):
    """resource:"path" reference."""


class Enum(str):
    """Quoted enum/string (same as str, kept for readability)."""


def kv3(value, indent=0):
    pad = "\t" * indent
    if isinstance(value, dict):
        lines = ["{"]
        for key, item in value.items():
            lines.append(f"{pad}\t{key} = {kv3(item, indent + 1)}")
        lines.append(pad + "}")
        return "\n".join(lines)
    if isinstance(value, list):
        if all(isinstance(x, (int, float)) or x is None for x in value):
            return "[ " + ", ".join(kv3(x) for x in value) + " ]"
        lines = ["["]
        for item in value:
            lines.append(f"{pad}\t{kv3(item, indent + 1)},")
        lines.append(pad + "]")
        return "\n".join(lines)
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, Res):
        return f'resource:"{value}"'
    if isinstance(value, str):
        return f'"{value}"'
    if isinstance(value, float):
        return f"{value:.6f}".rstrip("0").rstrip(".") if value != int(value) else f"{value:.1f}"
    return str(value)


# ---------------------------------------------------------------- building blocks
def lit(v):
    return {"m_nType": "PF_TYPE_LITERAL", "m_flLiteralValue": float(v)}


def rnd(a, b):
    return {"m_nType": "PF_TYPE_RANDOM_UNIFORM", "m_flRandomMin": float(a), "m_flRandomMax": float(b),
            "m_nRandomMode": "PF_RANDOM_MODE_CONSTANT"}


def vlit(x, y, z):
    return {"m_nType": "PVEC_TYPE_LITERAL", "m_vLiteralValue": [float(x), float(y), float(z)]}


LIFETIME, RADIUS, ROLL, ALPHA = 1, 3, 4, 7


def init_float(field, value):
    return {"_class": "C_INIT_InitFloat", "m_InputValue": value, "m_nOutputField": field}


def color(c1, c2):
    return {"_class": "C_INIT_RandomColor", "m_ColorMin": [*c1, None], "m_ColorMax": [*c2, None]}


def sphere(rmin, rmax, smin=0, smax=0, local_min=(0, 0, 0), local_max=(0, 0, 0), flat=False):
    init = {"_class": "C_INIT_CreateWithinSphereTransform",
            "m_fRadiusMin": lit(rmin), "m_fRadiusMax": lit(rmax),
            "m_fSpeedMin": lit(smin), "m_fSpeedMax": lit(smax),
            "m_LocalCoordinateSystemSpeedMin": vlit(*local_min),
            "m_LocalCoordinateSystemSpeedMax": vlit(*local_max)}
    if flat:
        init["m_vecDistanceBias"] = [1.0, 1.0, 0.0]
    return init


def offset(zmin, zmax, xy=0.0):
    return {"_class": "C_INIT_PositionOffset", "m_OffsetMin": [-xy, -xy, float(zmin)],
            "m_OffsetMax": [xy, xy, float(zmax)]}


def movement(gravity=0.0, drag=0.0):
    return {"_class": "C_OP_BasicMovement", "m_Gravity": [0.0, 0.0, float(gravity)], "m_fDrag": float(drag)}


def fade_out(proportional_min=0.25, proportional_max=0.4):
    return {"_class": "C_OP_FadeOut", "m_flFadeOutTimeMin": proportional_min, "m_flFadeOutTimeMax": proportional_max}


def fade_in(t=0.2):
    return {"_class": "C_OP_FadeIn", "m_flFadeInTimeMin": t, "m_flFadeInTimeMax": t}


def radius_ramp(start, end, bias=0.5):
    return {"_class": "C_OP_InterpolateRadius", "m_flStartScale": float(start), "m_flEndScale": float(end),
            "m_flBias": bias}


def color_fade(c):
    return {"_class": "C_OP_ColorInterpolate", "m_ColorFade": [*c, None]}


def random_force(f):
    return {"_class": "C_OP_RandomForce", "m_MinForce": [-f, -f, -f / 2], "m_MaxForce": [f, f, f / 2]}


def sprites(texture, additive=True, animation=None):
    r = {"_class": "C_OP_RenderSprites", "m_vecTexturesInput": [{"m_hTexture": Res(texture)}]}
    if additive:
        r["m_nOutputBlendMode"] = "PARTICLE_OUTPUT_BLEND_MODE_ADD"
    if animation:
        r["m_flAnimationRate"] = float(animation)
    return r


def ropes(texture, overbright, v_world=400.0, scroll=0.0):
    r = {"_class": "C_OP_RenderRopes", "m_flOverbrightFactor": float(overbright),
         "m_flTextureVWorldSize": float(v_world), "m_nMaxTesselation": 3, "m_nMinTesselation": 3,
         "m_vecTexturesInput": [{"m_hTexture": Res(texture)}],
         "m_nOutputBlendMode": "PARTICLE_OUTPUT_BLEND_MODE_ADD"}
    if scroll:
        r["m_flTextureVScrollRate"] = float(scroll)
    return r


def instant(n):
    return {"_class": "C_OP_InstantaneousEmitter", "m_nParticlesToEmit": lit(n)}


def continuous(rate, duration=None):
    e = {"_class": "C_OP_ContinuousEmitter", "m_flEmitRate": lit(rate)}
    if duration is not None:
        e["m_flEmissionDuration"] = lit(duration)
    return e


def system(max_particles, emitters, initializers, operators, renderers, forces=None, children=None, bounds=128):
    s = {"_class": "CParticleSystemDefinition",
         "m_nMaxParticles": max_particles,
         "m_BoundingBoxMin": [-float(bounds), -float(bounds), -16.0],
         "m_BoundingBoxMax": [float(bounds), float(bounds), float(bounds) * 2],
         "m_Renderers": renderers, "m_Operators": operators,
         "m_Initializers": initializers, "m_Emitters": emitters,
         "m_nBehaviorVersion": 12}
    if forces:
        s["m_ForceGenerators"] = forces
    if children:
        s["m_Children"] = [{"m_ChildRef": Res(f"particles/warcraft/{c}.vpcf")} for c in children]
    return s


# ---------------------------------------------------------------- archetypes
def flash(c, hi, size=70):
    """Single bright glow that swells and fades; child of bursts/columns/novas."""
    return system(2, [instant(1)],
                  [init_float(LIFETIME, lit(0.35)), init_float(RADIUS, lit(size)),
                   init_float(ALPHA, lit(0.85)), color(hi, c), offset(0, 0)],
                  [movement(), {"_class": "C_OP_Decay"}, fade_out(0.6, 0.7), radius_ramp(0.35, 1.2)],
                  [sprites(SOFT)])


def motes(c, hi, radius=36, rate=18):
    """Looping motes rising from a ring at the feet (auras, totems)."""
    return system(64, [continuous(rate)],
                  [init_float(LIFETIME, rnd(1.0, 1.8)), init_float(RADIUS, rnd(2.5, 5.0)),
                   init_float(ALPHA, rnd(0.6, 1.0)), color(c, hi),
                   sphere(radius * 0.8, radius, local_min=(0, 0, 25), local_max=(0, 0, 60), flat=True),
                   offset(2, 10)],
                  [movement(10, 0.05), {"_class": "C_OP_Decay"}, fade_in(0.25), fade_out(), radius_ramp(1.0, 0.2)],
                  [sprites(GLOW)], forces=[random_force(40)], bounds=radius + 32)


def wisps(c, hi, radius=36):
    """Slow large soft glows that make the aura read as a coloured haze."""
    return system(12, [continuous(3)],
                  [init_float(LIFETIME, rnd(1.4, 2.0)), init_float(RADIUS, rnd(14, 22)),
                   init_float(ALPHA, rnd(0.18, 0.3)), color(c, c),
                   sphere(radius * 0.5, radius, local_min=(0, 0, 8), local_max=(0, 0, 20), flat=True),
                   offset(4, 16)],
                  [movement(0, 0.1), {"_class": "C_OP_Decay"}, fade_in(0.4), fade_out(0.4, 0.5)],
                  [sprites(SOFT)], bounds=radius + 32)


def body(c, hi, rate=26):
    """Looping particles swirling up around a player's body (buffs, debuffs)."""
    return system(64, [continuous(rate)],
                  [init_float(LIFETIME, rnd(0.6, 1.2)), init_float(RADIUS, rnd(2.0, 4.0)),
                   init_float(ALPHA, rnd(0.7, 1.0)), color(c, hi),
                   sphere(12, 20, local_min=(0, 0, 10), local_max=(0, 0, 45), flat=True),
                   offset(6, 62)],
                  [movement(0, 0.08), {"_class": "C_OP_Decay"}, fade_in(0.2), fade_out(), radius_ramp(1.0, 0.3)],
                  [sprites(GLOW)], forces=[random_force(30)], bounds=48)


def body_glow(c, hi):
    return system(6, [continuous(2)],
                  [init_float(LIFETIME, lit(1.0)), init_float(RADIUS, rnd(26, 32)),
                   init_float(ALPHA, rnd(0.15, 0.22)), color(c, c), offset(36, 40)],
                  [movement(), {"_class": "C_OP_Decay"}, fade_in(0.4), fade_out(0.4, 0.5)],
                  [sprites(SOFT)], bounds=48)


def burst(c, hi, count=40):
    """One-shot sparkle explosion (hits, heals, casts)."""
    return system(64, [instant(count)],
                  [init_float(LIFETIME, rnd(0.5, 1.0)), init_float(RADIUS, rnd(2.0, 5.0)),
                   init_float(ALPHA, rnd(0.8, 1.0)), color(c, hi),
                   sphere(4, 12, 90, 220)],
                  [movement(-80, 0.07), {"_class": "C_OP_Decay"}, fade_out(), radius_ramp(1.0, 0.2)],
                  [sprites(GLOW)], bounds=96)


def ring_wave(c, hi, speed=480, count=56):
    """Glowing ring racing outwards along the ground."""
    return system(count + 8, [instant(count)],
                  [init_float(LIFETIME, rnd(0.45, 0.6)), init_float(RADIUS, rnd(8, 14)),
                   init_float(ALPHA, rnd(0.7, 1.0)), color(c, hi),
                   {"_class": "C_INIT_RingWave", "m_flInitialRadius": lit(8.0),
                    "m_flInitialSpeedMin": lit(speed), "m_flInitialSpeedMax": lit(speed)},
                   offset(4, 6)],
                  [movement(0, 0.12), {"_class": "C_OP_Decay"}, fade_out(0.5, 0.6), radius_ramp(0.6, 1.6)],
                  [sprites(GLOW)], bounds=300)


def radius_ring(c, hi):
    """Looping shimmer on a circle whose radius is CP1.x (totem/aura area, set by the server)."""
    return system(96, [continuous(45)],
                  [init_float(LIFETIME, rnd(0.9, 1.3)), init_float(RADIUS, rnd(3.0, 5.0)),
                   init_float(ALPHA, rnd(0.6, 0.9)), color(c, hi),
                   {"_class": "C_INIT_RingWave",
                    "m_flInitialRadius": {"m_nType": "PF_TYPE_CONTROL_POINT_COMPONENT",
                                          "m_nControlPoint": 1, "m_nVectorComponent": 0},
                    "m_flInitialSpeedMin": lit(0.0), "m_flInitialSpeedMax": lit(0.0)},
                   offset(2, 6)],
                  [movement(25, 0.1), {"_class": "C_OP_Decay"}, fade_in(0.25), fade_out(), radius_ramp(1.0, 0.3)],
                  [sprites(GLOW)], bounds=512)


def dust_ring(c):
    grey = tuple(int(v * 0.35 + 110 * 0.65) for v in c)
    return system(40, [instant(32)],
                  [init_float(LIFETIME, rnd(0.8, 1.1)), init_float(RADIUS, rnd(12, 20)),
                   init_float(ALPHA, rnd(0.25, 0.4)), init_float(ROLL, rnd(0, 360)), color(grey, grey),
                   {"_class": "C_INIT_RingWave", "m_flInitialRadius": lit(8.0),
                    "m_flInitialSpeedMin": lit(380.0), "m_flInitialSpeedMax": lit(420.0)},
                   offset(6, 10)],
                  [movement(8, 0.15), {"_class": "C_OP_Decay"}, fade_out(0.6, 0.7), radius_ramp(0.8, 2.2)],
                  [sprites(SMOKE, additive=False)], bounds=300)


def column(c, hi):
    """Pillar of light rising from the ground (resurrect, smite, recall)."""
    return system(96, [continuous(140, 0.5)],
                  [init_float(LIFETIME, rnd(0.35, 0.7)), init_float(RADIUS, rnd(6, 12)),
                   init_float(ALPHA, rnd(0.5, 0.9)), color(c, hi),
                   sphere(0, 10, local_min=(0, 0, 80), local_max=(0, 0, 220), flat=True),
                   offset(0, 150)],
                  [movement(0, 0.05), {"_class": "C_OP_Decay"}, fade_in(0.1), fade_out(), radius_ramp(1.0, 0.3)],
                  [sprites(GLOW)], bounds=64)


def line(c, hi, texture, jitter, width, overbright, life=0.3):
    """Rope between CP0 (entity origin) and CP1 (server control point)."""
    return system(20, [instant(16)],
                  [{"_class": "C_INIT_CreateSequentialPath", "m_flNumToAssign": 16.0,
                    "m_PathParams": {"m_nEndControlPointNumber": 1}},
                   color(c, hi), init_float(LIFETIME, lit(life)), init_float(RADIUS, rnd(width, width * 1.6)),
                   init_float(ALPHA, rnd(0.8, 1.0)),
                   {"_class": "C_INIT_PositionOffset", "m_OffsetMin": [-jitter, -jitter, -jitter / 2],
                    "m_OffsetMax": [jitter, jitter, jitter / 2]}],
                  [movement(), {"_class": "C_OP_Decay"}, fade_out(0.5, 0.6), radius_ramp(1.0, 1.8)],
                  [ropes(texture, overbright)], bounds=512)


def line_glow(c, hi):
    """Soft glow sprites strung along the same path."""
    return system(28, [instant(24)],
                  [{"_class": "C_INIT_CreateSequentialPath", "m_flNumToAssign": 24.0,
                    "m_PathParams": {"m_nEndControlPointNumber": 1}},
                   color(c, hi), init_float(LIFETIME, rnd(0.25, 0.4)), init_float(RADIUS, rnd(6, 10)),
                   init_float(ALPHA, rnd(0.3, 0.5))],
                  [movement(), {"_class": "C_OP_Decay"}, fade_out(0.5, 0.6)],
                  [sprites(SOFT)], bounds=512)


def flames(c, hi, radius, rate, size):
    """Looping fire tongues on a ring (immolation) or a point (brazier)."""
    return system(48, [continuous(rate)],
                  [init_float(LIFETIME, rnd(0.6, 1.0)), init_float(RADIUS, rnd(size, size * 1.6)),
                   init_float(ALPHA, rnd(0.7, 0.95)), init_float(ROLL, rnd(-15, 15)), color(hi, c),
                   sphere(radius * 0.7, radius, local_min=(0, 0, 40), local_max=(0, 0, 80), flat=True),
                   offset(0, 8)],
                  [movement(30, 0.05), {"_class": "C_OP_Decay"}, fade_in(0.15), fade_out(0.3, 0.45),
                   radius_ramp(1.0, 0.3)],
                  [sprites(FIRE, animation=1.5)], forces=[random_force(25)], bounds=radius + 48)


def snow(radius=120):
    return system(80, [continuous(22)],
                  [init_float(LIFETIME, rnd(2.5, 3.5)), init_float(RADIUS, rnd(1.5, 3.0)),
                   init_float(ALPHA, rnd(0.7, 1.0)), color((200, 230, 255), (255, 255, 255)),
                   sphere(0, radius, local_min=(0, 0, -40), local_max=(0, 0, -25), flat=True),
                   offset(90, 110)],
                  [movement(0, 0.02), {"_class": "C_OP_Decay"}, fade_in(0.3), fade_out()],
                  [sprites(SNOW, additive=False)], forces=[random_force(20)], bounds=radius + 16)


def smoke(c, hi):
    return system(24, [instant(16)],
                  [init_float(LIFETIME, rnd(0.9, 1.5)), init_float(RADIUS, rnd(14, 22)),
                   init_float(ALPHA, rnd(0.4, 0.6)), init_float(ROLL, rnd(0, 360)), color(c, hi),
                   sphere(4, 18, 20, 70), offset(10, 50)],
                  [movement(15, 0.1), {"_class": "C_OP_Decay"}, fade_out(0.6, 0.7), radius_ramp(0.8, 2.2)],
                  [sprites(SMOKE, additive=False)], bounds=96)


def sparks(c, hi):
    return system(40, [instant(28)],
                  [init_float(LIFETIME, rnd(0.3, 0.6)), init_float(RADIUS, rnd(1.0, 2.2)),
                   init_float(ALPHA, lit(1.0)), color(hi, c), sphere(1, 4, 150, 360)],
                  [movement(-600, 0.03), {"_class": "C_OP_Decay"}, fade_out(0.3, 0.4), color_fade(c)],
                  [sprites(GLOW)], bounds=96)


# ---------------------------------------------------------------- library
def library():
    fx = {}
    for name, (c, hi) in PALETTE.items():
        fx[f"flash_{name}"] = flash(c, hi)
        fx[f"motes_{name}"] = motes(c, hi) | {}
        fx[f"motes_{name}_wisps"] = wisps(c, hi)
        fx[f"motes_{name}"]["m_Children"] = [{"m_ChildRef": Res(f"particles/warcraft/motes_{name}_wisps.vpcf")}]
        fx[f"body_{name}_glow"] = body_glow(c, hi)
        fx[f"body_{name}"] = body(c, hi)
        fx[f"body_{name}"]["m_Children"] = [{"m_ChildRef": Res(f"particles/warcraft/body_{name}_glow.vpcf")}]
        fx[f"burst_{name}"] = burst(c, hi)
        fx[f"burst_{name}"]["m_Children"] = [{"m_ChildRef": Res(f"particles/warcraft/flash_{name}.vpcf")}]
        fx[f"nova_{name}_dust"] = dust_ring(c)
        fx[f"nova_{name}"] = ring_wave(c, hi)
        fx[f"nova_{name}"]["m_Children"] = [
            {"m_ChildRef": Res(f"particles/warcraft/nova_{name}_dust.vpcf")},
            {"m_ChildRef": Res(f"particles/warcraft/flash_{name}.vpcf")}]
        fx[f"column_{name}"] = column(c, hi)
        fx[f"column_{name}"]["m_Children"] = [
            {"m_ChildRef": Res(f"particles/warcraft/burst_{name}.vpcf")}]
        fx[f"line_{name}_glow"] = line_glow(c, hi)
        texture, jitter, width, overbright = {
            "storm": (BOLT, 10.0, 3.0, 12.0),
            "fire": (ENERGY, 0.0, 1.5, 8.0),
        }.get(name, (ENERGY, 2.0, 3.0, 6.0))
        fx[f"line_{name}"] = line(c, hi, texture, jitter, width, overbright)
        fx[f"line_{name}"]["m_Children"] = [{"m_ChildRef": Res(f"particles/warcraft/line_{name}_glow.vpcf")}]
        fx[f"radius_{name}"] = radius_ring(c, hi)
        fx[f"sparks_{name}"] = sparks(c, hi)
        fx[f"smoke_{name}"] = smoke(c, hi)

    fire, fire_hi = PALETTE["fire"]
    fx["flames_ring"] = flames(fire, fire_hi, radius=34, rate=22, size=9)
    fx["flames_ring"]["m_Children"] = [{"m_ChildRef": Res("particles/warcraft/motes_fire.vpcf")}]
    fx["flames_brazier"] = flames(fire, fire_hi, radius=6, rate=16, size=7)
    war, war_hi = PALETTE["war"]
    fx["flames_rage"] = flames(war, war_hi, radius=16, rate=20, size=7)
    fx["flames_rage"]["m_Children"] = [{"m_ChildRef": Res("particles/warcraft/body_war.vpcf")}]
    fx["snow"] = snow()
    fx["snow"]["m_Children"] = [{"m_ChildRef": Res("particles/warcraft/motes_frost.vpcf")}]
    return fx


def main():
    if os.path.isdir(ROOT):
        shutil.rmtree(ROOT)
    os.makedirs(ROOT)
    fx = library()
    for name, definition in sorted(fx.items()):
        with open(os.path.join(ROOT, f"{name}.vpcf"), "w", encoding="utf-8", newline="\n") as f:
            f.write(HEADER + kv3(definition) + "\n")
    print(f"wrote {len(fx)} particle systems to {os.path.normpath(ROOT)}")


if __name__ == "__main__":
    main()
