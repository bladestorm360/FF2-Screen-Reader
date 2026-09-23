"""
Merge an authored translation batch into this mod's embedded translation.json.

A batch is a JSON object { japaneseKey: { lang: value, ... } } covering the 11 authored
languages (en fr it de es ko zht zhc ru th pt). 'ja' is never stored: EntityTranslator
returns the raw name untouched when the game runs in Japanese.

Every batch is validated before anything is written:
  * all 11 languages present and non-empty
  * each value is in a plausible script for its language (Hangul for ko, Cyrillic for
    ru, Thai for th, CJK for zht/zhc, no kana anywhere outside the key)
  * no stray whitespace
Existing entries are never overwritten unless --overwrite is given. New keys are
appended after the existing ones, so the diff is additions only; the file's line
endings are preserved.

Usage:
  python apply_translations.py check <batch.json>
  python apply_translations.py apply <batch.json> [--overwrite]
"""
import json, os, re, sys

MOD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TRANSLATION = os.path.join(MOD, "translation.json")
LANGS = ["en", "fr", "it", "de", "es", "ko", "zht", "zhc", "ru", "th", "pt"]

KANA = re.compile(r"[぀-ヿ]")
HANGUL = re.compile(r"[가-힯]")
CYRILLIC = re.compile(r"[Ѐ-ӿ]")
THAI = re.compile(r"[฀-๿]")
CJK = re.compile(r"[一-鿿]")
LATIN_LANGS = {"en", "fr", "it", "de", "es", "pt"}


def problems(key, entry):
    out = []
    for lang in LANGS:
        v = entry.get(lang)
        if not isinstance(v, str) or not v.strip():
            out.append(f"{lang}: missing")
            continue
        if v != v.strip():
            out.append(f"{lang}: surrounding whitespace")
        if KANA.search(v):
            out.append(f"{lang}: contains kana: {v}")
        if lang in LATIN_LANGS and CJK.search(v):
            out.append(f"{lang}: contains CJK: {v}")
        if lang == "ko" and not HANGUL.search(v) and not re.search(r"[A-Za-z0-9]", v):
            out.append(f"ko: no Hangul: {v}")
        if lang == "ru" and not CYRILLIC.search(v) and not re.search(r"[A-Za-z0-9]", v):
            out.append(f"ru: no Cyrillic: {v}")
        if lang == "th" and not THAI.search(v) and not re.search(r"[A-Za-z0-9]", v):
            out.append(f"th: no Thai: {v}")
        if lang in ("zht", "zhc") and not CJK.search(v) and not re.search(r"[A-Za-z0-9]", v):
            out.append(f"{lang}: no CJK: {v}")
    extra = set(entry) - set(LANGS) - {"ja"}
    if extra:
        out.append(f"unexpected languages {sorted(extra)}")
    return out


def load_batch(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def check(batch):
    bad = {k: p for k, e in batch.items() if (p := problems(k, e))}
    for k, p in bad.items():
        print(f"  {k}: {'; '.join(p)}")
    print(f"{len(batch)} entries, {len(bad)} with problems")
    return not bad


def render_entry(key, entry, newline):
    body = json.dumps({key: {lang: entry[lang] for lang in LANGS}}, ensure_ascii=False, indent=2)
    # strip the wrapping braces of the one-key object, keep the 2-space indented member
    inner = body.split("\n")[1:-1]
    return newline.join(inner)


def apply(batch, overwrite):
    """Appends new entries textually before the closing brace, so existing bytes are
    untouched. --overwrite falls back to a full re-serialisation."""
    raw = open(TRANSLATION, "rb").read()
    text = raw.decode("utf-8-sig")
    newline = "\r\n" if "\r\n" in text else "\n"
    data = json.loads(text)

    new_keys = [k for k in batch if k not in data]
    existing = [k for k in batch if k in data]

    if overwrite and existing:
        for k in batch:
            data[k] = {lang: batch[k][lang] for lang in LANGS}
        out = (json.dumps(data, ensure_ascii=False, indent=2) + "\n").replace("\n", newline)
        print(f"re-serialised: added {len(new_keys)}, replaced {len(existing)}")
    else:
        close = text.rstrip().rfind("}")
        head, tail = text[:close].rstrip(), text[close:]
        members = [render_entry(k, batch[k], newline) for k in new_keys]
        sep = "," + newline
        out = head + (sep if members else "") + sep.join(members) + newline + tail
        print(f"appended {len(new_keys)}, skipped existing {len(existing)}")

    json.loads(out)  # never write a file that does not parse
    with open(TRANSLATION, "w", encoding="utf-8", newline="") as f:
        f.write(out)
    print(f"translation.json now has {len(json.loads(out))} entries")


if __name__ == "__main__":
    if len(sys.argv) < 3 or sys.argv[1] not in ("check", "apply"):
        print(__doc__)
        sys.exit(1)
    b = load_batch(sys.argv[2])
    if not check(b):
        sys.exit(2)
    if sys.argv[1] == "apply":
        apply(b, "--overwrite" in sys.argv)
