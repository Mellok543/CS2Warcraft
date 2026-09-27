"""Generate CS2Warcraft visual models with Meshy (text-to-3D preview + refine).

Key comes from MESHY_API_KEY. Task ids are stored in a state file so a rerun
resumes without paying twice. Outputs raw OBJ + base color PNG per asset.
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

API = "https://api.meshy.ai/openapi/v2/text-to-3d"
KEY = os.environ["MESHY_API_KEY"]
OUT = sys.argv[1]
STATE = os.path.join(OUT, "meshy_state.json")

STYLE = ("stylized hand-painted fantasy game asset in the style of Warcraft, "
         "clean silhouette, no ground plane, no text")

ASSETS = {
    "totem_healing": "a shaman healing totem, single object standing upright: carved wooden totem pole with green "
                     "glowing spirit runes, leaves and vines wrapped around it, a small green crystal on top",
    "totem_flame": "a shaman fire totem, single object standing upright: dark carved stone totem pole with a burning "
                   "brazier bowl on top and glowing orange lava runes",
    "totem_frost": "a shaman frost totem, single object standing upright: carved pillar of dark frozen stone and blue "
                   "ice, sharp ice crystals on top, glowing cyan abstract snowflake symbols, no letters, no writing",
    "totem_war": "an orc war totem, single object standing upright: wooden totem pole with a red war banner, a horned "
                 "animal skull and two crossed axes on top, iron spikes",
    "totem_shield": "a guardian protection totem, single object standing upright: carved golden stone pillar with a "
                    "round shield emblem, glowing golden holy runes, small wings on top",
    "entangle_roots": "a snare trap made of a low ring of thick twisted thorny vines and roots coiling on the ground "
                      "around an empty hollow center, no tree, no trunk, no canopy, flat and wide, glowing green "
                      "thorn tips",
    "turret": "a dwarven engineer auto turret, single object standing on the ground: sturdy tripod legs of riveted "
              "iron, a rotating brass and steel gun head with a twin barrel cannon pointing forward, glowing orange "
              "core, gears and pipes, compact",
    # Cosmetics (hats, backpacks, shoulder pets, masks) — not wired to gameplay yet.
    "hat_wizard": "a single hat on its own, no head, no mannequin, no face: tall pointed purple wizard hat with a wide brim, silver stars and a glowing blue gem buckle",
    "hat_viking": "a single hat on its own, no head, no mannequin, no face: iron viking helmet with two big curved horns, leather trim and brass rivets",
    "hat_crown": "a single hat on its own, no head, no mannequin, no face: ornate royal golden crown with red rubies and blue sapphires",
    "hat_cat_headset": "a single hat on its own, no head, no mannequin, no face: trendy gamer headset with cute cat ears glowing pink and cyan RGB lights",
    "hat_mushroom": "a wearable hat only, no head, no stem, no ground, no grass: wide dome shaped red mushroom cap with white spots worn as a hat, hollow underside, trendy cottagecore style",
    "backpack_loot_sack": "a single backpack on its own, no person, straps at the back: goblin loot sack stuffed with gold coins, a sword hilt and a scroll sticking out, patched cloth",
    "backpack_jetpack": "a single backpack on its own, no person, straps at the back: dwarven steampunk jetpack with two brass rocket thrusters, pipes and a pressure gauge",
    "backpack_quiver": "a single backpack on its own, no person, straps at the back: elven leather quiver full of arrows with green feathers and golden leaf ornaments",
    "backpack_mimic": "a single backpack on its own, no person, straps at the back: treasure chest mimic backpack with sharp teeth and a long tongue, wooden chest with gold bands",
    "backpack_capybara": "a single backpack on its own, no person, straps at the back: trendy cute plush capybara backpack, soft brown fur, calm sleepy face, small orange on its head",
    "pet_dragon": "a small creature sitting calmly, compact rounded pose to sit on a player's shoulder, single creature: baby red dragon with small wings, big eyes and tiny horns",
    "pet_owl": "a small creature sitting calmly, compact rounded pose to sit on a player's shoulder, single creature: snowy white owl with big golden eyes and fluffy feathers",
    "pet_capybara": "a small cute capybara lying down relaxed on its belly, barrel shaped body, short legs tucked in, blunt square snout, small round ears, brown fur, a small orange fruit balanced on its head, calm sleepy half closed eyes, trendy meme style, single creature",
    "pet_axolotl": "a small creature sitting calmly, compact rounded pose to sit on a player's shoulder, single creature: trendy cute pink axolotl with frilly gills and a happy smile",
    "pet_cowboy_frog": "a small creature sitting calmly, compact rounded pose to sit on a player's shoulder, single creature: trendy cute green frog wearing a tiny brown cowboy hat",
    "mask_kitsune": "a single face mask on its own, no head, front facing, eye holes: japanese kitsune fox mask, white with red and gold markings",
    "mask_oni": "a single face mask on its own, no head, front facing, eye holes: red japanese oni demon mask with horns and fangs",
    "mask_cyber": "a single face mask on its own, no head, front facing, eye holes: trendy cyberpunk LED face mask, black with glowing neon pixel eyes and smile",
    "mask_plague": "a single face mask on its own, no head, front facing, eye holes: plague doctor mask with a long leather beak and round brass goggles",
    "mask_orc": "a single face mask on its own, no head, front facing, eye holes: orc war mask made of dark iron with big bone tusks and red war paint",
}


def request(method, url, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": f"Bearer {KEY}",
        "Content-Type": "application/json",
    })
    # Meshy limits concurrent tasks and request rate (HTTP 429): wait and retry.
    for attempt in range(40):
        try:
            with urllib.request.urlopen(req, timeout=60) as response:
                return json.loads(response.read().decode())
        except urllib.error.HTTPError as error:
            if error.code != 429 or attempt == 39:
                raise
            print(f"rate limited, retrying in 30 s ({url})", flush=True)
            time.sleep(30)


def wait(task_id, label):
    while True:
        task = request("GET", f"{API}/{task_id}")
        status = task.get("status")
        if status == "SUCCEEDED":
            print(f"{label}: SUCCEEDED ({task.get('consumed_credits')} credits)", flush=True)
            return task
        if status in ("FAILED", "CANCELED"):
            raise RuntimeError(f"{label}: {status} {task.get('task_error')}")
        print(f"{label}: {status} {task.get('progress')}%", flush=True)
        time.sleep(15)


def download(url, path):
    with urllib.request.urlopen(url, timeout=300) as response, open(path, "wb") as target:
        target.write(response.read())


def main():
    os.makedirs(OUT, exist_ok=True)
    state = json.load(open(STATE)) if os.path.exists(STATE) else {}

    def save():
        json.dump(state, open(STATE, "w"), indent=2)

    for name, prompt in ASSETS.items():
        entry = state.setdefault(name, {})
        if "preview" not in entry:
            result = request("POST", API, {
                "mode": "preview",
                "prompt": f"{prompt}, {STYLE}",
                "ai_model": "latest",
                "topology": "triangle",
                "should_remesh": True,
                "target_polycount": 6000,
            })
            entry["preview"] = result["result"]
            save()
            print(f"{name}: preview task {entry['preview']}", flush=True)

    for name, prompt in ASSETS.items():
        entry = state[name]
        if "refine" not in entry:
            wait(entry["preview"], f"{name} preview")
            result = request("POST", API, {
                "mode": "refine",
                "preview_task_id": entry["preview"],
                "enable_pbr": False,
                "texture_prompt": f"{prompt}, vivid hand-painted colors",
            })
            entry["refine"] = result["result"]
            save()
            print(f"{name}: refine task {entry['refine']}", flush=True)

    for name in ASSETS:
        entry = state[name]
        folder = os.path.join(OUT, name)
        if os.path.exists(os.path.join(folder, "model.obj")) and os.path.exists(os.path.join(folder, "texture.png")):
            continue

        task = wait(entry["refine"], f"{name} refine")
        os.makedirs(folder, exist_ok=True)
        download(task["model_urls"]["obj"], os.path.join(folder, "model.obj"))
        download(task["texture_urls"][0]["base_color"], os.path.join(folder, "texture.png"))
        print(f"{name}: downloaded", flush=True)

    print("ALL DONE", flush=True)


if __name__ == "__main__":
    main()
