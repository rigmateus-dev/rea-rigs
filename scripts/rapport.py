#!/usr/bin/env python3
"""Rapport de mise a jour de REA (lu par Rig's avant d'accepter).

Usage : rapport.py ANCIEN_DOSSIER NOUVEAU_DOSSIER ANCIENNE_VERSION NOUVELLE_VERSION > rapport.md
Les dossiers sont des paquets npm extraits (npm pack + tar). Rien n'est execute.
"""
import json
import re
import sys
from pathlib import Path

# ce qui merite un coup d'oeil : lancer des programmes, parler au reseau, lire l'environnement
SENSIBLE = {
    "lance des programmes": re.compile(r"\bchild_process\b|\bspawn\s*\(|\bexecFile\s*\(|\bexec\s*\(|\bfork\s*\("),
    "reseau": re.compile(r"\bfetch\s*\(|\bhttps?\.request\s*\(|\bnet\.connect\s*\(|\bnew WebSocket\s*\(|\bdgram\b"),
    "variables d'environnement": re.compile(r"\bprocess\.env\b"),
    "code dynamique": re.compile(r"\beval\s*\(|\bnew Function\s*\(|\bvm\.run"),
}


def deps(root: Path) -> dict:
    f = root / "npm-shrinkwrap.json"
    if not f.is_file():
        return {}
    pk = json.loads(f.read_text(encoding="utf-8")).get("packages", {})
    out = {}
    for k, v in pk.items():
        if k.startswith("node_modules/") and not v.get("dev"):
            out[k[len("node_modules/"):]] = (v.get("version", "?"), bool(v.get("hasInstallScript")))
    return out


def scan(root: Path) -> tuple[dict, set]:
    counts = {k: 0 for k in SENSIBLE}
    files = set()
    for p in root.rglob("*"):
        if not p.is_file():
            continue
        rel = p.relative_to(root).as_posix()
        files.add(rel)
        if p.suffix not in (".js", ".mjs", ".cjs"):
            continue
        try:
            txt = p.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for k, rx in SENSIBLE.items():
            counts[k] += len(rx.findall(txt))
    return counts, files


def main() -> int:
    old, new, vo, vn = Path(sys.argv[1]), Path(sys.argv[2]), sys.argv[3], sys.argv[4]
    d_old, d_new = deps(old), deps(new)
    added = sorted(set(d_new) - set(d_old))
    removed = sorted(set(d_old) - set(d_new))
    changed = sorted(k for k in set(d_new) & set(d_old) if d_new[k][0] != d_old[k][0])
    scripts = sorted(k for k in d_new if d_new[k][1] and (k in added or not d_old.get(k, ("", False))[1]))
    c_old, f_old = scan(old)
    c_new, f_new = scan(new)
    w = sys.stdout.write
    w(f"## Mise a jour REA {vo} -> {vn}\n\n")
    w("Changements de l'auteur : https://github.com/morluto/rea/blob/main/CHANGELOG.md\n\n")
    alert = bool(added) or bool(scripts) or any(c_new[k] > c_old[k] for k in ("reseau", "code dynamique"))
    w(("**A regarder de pres** (voir ci-dessous)\n\n" if alert else "**Rien d'inhabituel detecte.**\n\n"))
    w(f"### Dependances\n- ajoutees : {len(added)}\n- retirees : {len(removed)}\n- changees de version : {len(changed)}\n")
    if scripts:
        w(f"- **avec script d'installation (bloque par --ignore-scripts)** : {', '.join(scripts)}\n")
    for k in added[:40]:
        w(f"  - + `{k}` {d_new[k][0]}\n")
    if len(added) > 40:
        w(f"  - ... et {len(added) - 40} autres\n")
    w("\n### Code sensible (nombre d'endroits, ancien -> nouveau)\n")
    for k in SENSIBLE:
        flag = "  ⚠" if c_new[k] > c_old[k] else ""
        w(f"- {k} : {c_old[k]} -> {c_new[k]}{flag}\n")
    w(f"\n### Fichiers\n- nouveaux : {len(f_new - f_old)}\n- supprimes : {len(f_old - f_new)}\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
