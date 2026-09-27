"""Generate CS2Warcraft visual models with Meshy (text-to-3D preview + refine).

Key comes from MESHY_API_KEY. Task ids are stored in a state file so a rerun
resumes without paying twice. Outputs raw OBJ + base color PNG per asset.
"""
import json
import os
import sys
import time
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
}


def request(method, url, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": f"Bearer {KEY}",
        "Content-Type": "application/json",
    })
    with urllib.request.urlopen(req, timeout=60) as response:
        return json.loads(response.read().decode())


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
