#!/usr/bin/env python3
"""
Generate a new TaskNode (or TaskCondition) .cs file from a template.

Usage examples:

# Simple TaskNode (no config):
python new_task_node.py \
  --name StopMusicNode \
  --display-name "Stop Music" \
  --category "Audio" \
  --description "Stops background music. Ref: source (AudioSource)." \
  --base TaskNode \
  --refs "source:AudioSource" \
  --path "Path/Chosen/By/User/StopMusicNode.cs --namespace MyProject.Audio"

# TaskNode<T> with config:
python new_task_node.py \
  --name ShakeCameraNode \
  --display-name "Shake Camera" \
  --category "Gameplay" \
  --description "Shakes the camera. Ref: target (Camera). Config: duration, strength." \
  --base TaskNodeT \
  --refs "target:Camera" \
  --config-fields "duration:float=0.3;strength:float=0.5" \
  --path "Path/Chosen/By/User/ShakeCameraNode.cs" \
  --namespace MyProject.Features.Combat

# TaskCondition:
python new_task_node.py \
  --name IsPlayerLowHpCondition \
  --base TaskCondition \
  --description "True when player HP below threshold." \
  --refs "_player:Player" \
  --config-fields "threshold:float=0.3" \
  --path "Path/Chosen/By/User/IsPlayerLowHpCondition.cs" \
  --namespace MyProject.Features.Combat
"""

import argparse
import re
import sys
from pathlib import Path

SKILL_ROOT = Path(__file__).resolve().parent.parent
TEMPLATES_DIR = SKILL_ROOT / "assets" / "templates"

# ─── Helpers ────────────────────────────────────────────────────────────

def parse_pairs(text, kv_sep=":", default_sep="=", item_sep=";"):
    """Parse strings like 'name:Type=default;other:Type2'. Returns list of (name, type, default_or_None)."""
    if not text:
        return []
    out = []
    for item in text.split(item_sep):
        item = item.strip()
        if not item:
            continue
        # Split on '=' first (default value may contain ':')
        if default_sep in item:
            head, default = item.split(default_sep, 1)
        else:
            head, default = item, None
        if kv_sep not in head:
            raise ValueError(f"Invalid pair (missing '{kv_sep}'): {item}")
        name, typ = head.split(kv_sep, 1)
        out.append((name.strip(), typ.strip(), default.strip() if default else None))
    return out


def format_serialized_fields(refs):
    """Format [SerializeField] lines for the class body.

    `refs` is a list of (name, type, default_or_None). Default ignored (Unity refs start null)."""
    if not refs:
        return ""
    lines = []
    for name, typ, _ in refs:
        # Use camelCase for private fields unless already prefixed with underscore
        lines.append(f"        [SerializeField] private {typ} {name};")
    return "\n".join(lines) + "\n"


PRIMITIVE_TYPES = {"int", "float", "double", "long", "short", "byte",
                   "bool", "string", "char", "uint", "ulong",
                   "Vector2", "Vector3", "Vector4", "Color", "Quaternion"}


def _normalize_csharp_default(typ, default):
    """Adjust default literals for C# syntax: float suffix, enum type-prefix."""
    t = typ.strip()
    d = default.strip()
    # Numeric suffix for float — if bare number (no suffix), add 'f'
    if t == "float" and re.fullmatch(r"-?\d+(\.\d+)?", d):
        d += "f"
    # Enum — if type looks like a PascalCase enum (not primitive) and default is a bare identifier
    # without dot, prefix with the type name: `OnceAndWait` → `TaskPlayMode.OnceAndWait`
    if (t not in PRIMITIVE_TYPES
            and re.fullmatch(r"[A-Z][A-Za-z0-9_]*", t)
            and re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", d)
            and "." not in d
            and d not in {"true", "false", "null"}):
        d = f"{t}.{d}"
    return d


def format_config_fields(cfgs):
    """Format public fields inside Settings class.

    `cfgs` is a list of (name, type, default_or_None)."""
    if not cfgs:
        return "            // (no config fields yet)\n"
    lines = []
    for name, typ, default in cfgs:
        if default is not None:
            lines.append(f"            public {typ} {name} = {_normalize_csharp_default(typ, default)};")
        else:
            lines.append(f"            public {typ} {name};")
    return "\n".join(lines) + "\n"


def format_condition_fields(refs, cfgs):
    """TaskCondition uses a flat list of [SerializeField] fields — combine refs + cfgs as private fields."""
    if not refs and not cfgs:
        return ""
    lines = []
    for name, typ, _ in refs:
        lines.append(f"        [SerializeField] private {typ} {name};")
    for name, typ, default in cfgs:
        if default is not None:
            lines.append(f"        [SerializeField] private {typ} {name} = {default};")
        else:
            lines.append(f"        [SerializeField] private {typ} {name};")
    return "\n".join(lines) + "\n"


def _sanitize_ns_segment(seg):
    """Make a path segment a legal C# identifier fragment."""
    # Replace non-alphanumeric with nothing (hyphens, dots, etc.)
    s = re.sub(r"[^A-Za-z0-9_]", "", seg)
    # Leading digit? Prefix with '_'
    if s and s[0].isdigit():
        s = "N" + s
    return s


def derive_namespace(path_str):
    """Derive a namespace from directory segments under 'Scripts' / '0_Scripts' / 'src'.
    Returns only the path-derived segments joined with '.'. No project prefix is assumed —
    if the caller wants a root prefix (like 'MyCompany.'), pass --namespace explicitly."""
    p = Path(path_str).resolve()
    parts = p.parts
    for i, seg in enumerate(parts):
        if seg in ("0_Scripts", "Scripts", "src"):
            tail = [_sanitize_ns_segment(x) for x in parts[i + 1 : -1]]
            tail = [t for t in tail if t]
            if tail:
                return ".".join(tail)
            return ""
    # Fallback: last 2 directory segments (excluding filename), sanitized
    tail_raw = parts[-3:-1] if len(parts) >= 3 else parts[:-1]
    tail = [_sanitize_ns_segment(x) for x in tail_raw]
    tail = [t for t in tail if t]
    return ".".join(tail) if tail else ""


def validate_pascal(name, what):
    if not re.match(r"^[A-Z][A-Za-z0-9_]*$", name):
        sys.exit(f"Error: {what} must be PascalCase, got: {name!r}")


# ─── Main ───────────────────────────────────────────────────────────────

def main():
    ap = argparse.ArgumentParser(description="Generate a new TaskNode / TaskCondition .cs file.")
    ap.add_argument("--name", required=True, help="C# class name (PascalCase, e.g. ShakeCameraNode)")
    ap.add_argument("--display-name", default="", help='[DefineTaskNode] Id (display name in Add Child dropdown)')
    ap.add_argument("--category", default="", help='Optional [DefineTaskNode] Category')
    ap.add_argument("--description", default="", help='[DefineTaskNode] Description')
    ap.add_argument("--base", choices=["TaskNode", "TaskNodeT", "TaskCondition"], required=True,
                    help="Base class. TaskNode = no config. TaskNodeT = with Settings. TaskCondition = branch predicate.")
    ap.add_argument("--refs", default="",
                    help='[SerializeField] Unity refs. Format: "name:Type;name2:Type2". E.g. "target:Transform;source:AudioSource"')
    ap.add_argument("--config-fields", default="",
                    help='Settings pure-data fields (TaskNodeT) or SerializeField fields (TaskCondition). Format: "name:Type=default;name2:Type2"')
    ap.add_argument("--namespace", default="",
                    help="Override namespace (else derived from --path)")
    ap.add_argument("--path", required=True, help="Target .cs file path (absolute or project-relative)")
    ap.add_argument("--overwrite", action="store_true", help="Overwrite file if it exists")
    args = ap.parse_args()

    validate_pascal(args.name, "--name")

    # Parse refs and config
    try:
        refs = parse_pairs(args.refs)
        cfgs = parse_pairs(args.config_fields)
    except ValueError as e:
        sys.exit(f"Error parsing fields: {e}")

    # Resolve namespace: explicit flag beats path-derived. If nothing usable, fail with guidance.
    namespace = args.namespace or derive_namespace(args.path)
    if not namespace:
        sys.exit("Error: could not derive namespace from --path (no 'Scripts'/'0_Scripts'/'src' segment found). "
                 "Pass --namespace explicitly, e.g. --namespace MyCompany.Features.Combat")

    # Decide template + display/category handling
    if args.base == "TaskNode":
        template_name = "task-node-simple.cs.tmpl"
        if cfgs:
            sys.exit("Error: --config-fields not applicable to --base TaskNode. Use --base TaskNodeT.")
    elif args.base == "TaskNodeT":
        template_name = "task-node-with-config.cs.tmpl"
    elif args.base == "TaskCondition":
        template_name = "task-condition.cs.tmpl"
    else:
        sys.exit(f"Unknown base: {args.base}")

    template_path = TEMPLATES_DIR / template_name
    if not template_path.exists():
        sys.exit(f"Template not found: {template_path}")
    template = template_path.read_text(encoding="utf-8")

    # Build substitutions
    display_name = args.display_name or args.name
    category = args.category
    description = args.description

    if args.base == "TaskCondition":
        fields_block = format_condition_fields(refs, cfgs)
        subs = {
            "{{CLASS_NAME}}": args.name,
            "{{NAMESPACE}}": namespace,
            "{{DESCRIPTION}}": description.replace('"', '\\"'),
            "{{SERIALIZED_FIELDS}}": fields_block,
        }
    else:
        serialized_block = format_serialized_fields(refs)
        config_block = format_config_fields(cfgs) if args.base == "TaskNodeT" else ""
        subs = {
            "{{CLASS_NAME}}": args.name,
            "{{NAMESPACE}}": namespace,
            "{{DISPLAY_NAME}}": display_name.replace('"', '\\"'),
            "{{CATEGORY}}": category.replace('"', '\\"'),
            "{{DESCRIPTION}}": description.replace('"', '\\"'),
            "{{SERIALIZED_FIELDS}}": serialized_block,
            "{{CONFIG_FIELDS}}": config_block,
        }

    output = template
    for key, val in subs.items():
        output = output.replace(key, val)

    # Write file
    target = Path(args.path)
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists() and not args.overwrite:
        sys.exit(f"Error: {target} exists. Pass --overwrite to replace.")
    target.write_text(output, encoding="utf-8")
    print(f"✓ Wrote {target}")

    # Post-hints
    if args.base == "TaskNode" and not refs:
        print("  Hint: no refs provided. If the task needs Unity object refs, pass --refs.")
    if args.base == "TaskNodeT" and not cfgs:
        print("  Hint: no config fields. If the task has no knobs, consider --base TaskNode instead.")
    if args.base == "TaskCondition":
        print("  Next: attach this condition component to a child GameObject of your ConditionalNode.")
    else:
        print(f"  Next: add this component to a GameObject under a Sequential/Parallel node in your TaskTree.")


if __name__ == "__main__":
    main()
