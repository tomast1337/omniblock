import json

REPLACEMENTS = {
    "a": "󱤀",
    "akesi": "󱤁",
    "ala": "󱤂",
    "alasa": "󱤃",
    "ale": "󱤄",
    "anpa": "󱤅",
    "ante": "󱤆",
    "anu": "󱤇",
    "awen": "󱤈",
    "e": "󱤉",
    "en": "󱤊",
    "esun": "󱤋",
    "ijo": "󱤌",
    "ike": "󱤍",
    "ilo": "󱤎",
    "insa": "󱤏",
    "jaki": "󱤐",
    "jan": "󱤑",
    "jelo": "󱤒",
    "jo": "󱤓",
    "kala": "󱤔",
    "kalama": "󱤕",
    "kama": "󱤖",
    "kasi": "󱤗",
    "ken": "󱤘",
    "kepeken": "󱤙",
    "kili": "󱤚",
    "kipisi": "󱥻",
    "kiwen": "󱤛",
    "ko": "󱤜",
    "kon": "󱤝",
    "kule": "󱤞",
    "kulupu": "󱤟",
    "kute": "󱤠",
    "la": "󱤡",
    "lape": "󱤢",
    "laso": "󱤣",
    "lawa": "󱤤",
    "len": "󱤥",
    "lete": "󱤦",
    "li": "󱤧",
    "lili": "󱤨",
    "linja": "󱤩",
    "lipu": "󱤪",
    "loje": "󱤫",
    "lon": "󱤬",
    "luka": "󱤭",
    "lukin": "󱤮",
    "lupa": "󱤯",
    "ma": "󱤰",
    "mama": "󱤱",
    "mani": "󱤲",
    "meli": "󱤳",
    "mi": "󱤴",
    "mije": "󱤵",
    "moku": "󱤶",
    "moli": "󱤷",
    "monsi": "󱤸",
    "mu": "󱤹",
    "mun": "󱤺",
    "musi": "󱤻",
    "mute": "󱤼",
    "nanpa": "󱤽",
    "nasa": "󱤾",
    "nasin": "󱤿",
    "nena": "󱥀",
    "ni": "󱥁",
    "nimi": "󱥂",
    "noka": "󱥃",
    "o": "󱥄",
    "olin": "󱥅",
    "ona": "󱥆",
    "open": "󱥇",
    "pakala": "󱥈",
    "pali": "󱥉",
    "palisa": "󱥊",
    "pan": "󱥋",
    "pana": "󱥌",
    "pi": "󱥍",
    "pilin": "󱥎",
    "pimeja": "󱥏",
    "pini": "󱥐",
    "pipi": "󱥑",
    "poka": "󱥒",
    "poki": "󱥓",
    "pona": "󱥔",
    "pu": "󱥕",
    "sama": "󱥖",
    "seli": "󱥗",
    "selo": "󱥘",
    "seme": "󱥙",
    "sewi": "󱥚",
    "sijelo": "󱥛",
    "sike": "󱥜",
    "sin": "󱥝",
    "sina": "󱥞",
    "sinpin": "󱥟",
    "sitelen": "󱥠",
    "sona": "󱥡",
    "soweli": "󱥢",
    "suli": "󱥣",
    "suno": "󱥤",
    "supa": "󱥥",
    "suwi": "󱥦",
    "tan": "󱥧",
    "taso": "󱥨",
    "tawa": "󱥩",
    "telo": "󱥪",
    "tenpo": "󱥫",
    "toki": "󱥬",
    "tomo": "󱥭",
    "tu": "󱥮",
    "unpa": "󱥯",
    "uta": "󱥰",
    "utala": "󱥱",
    "walo": "󱥲",
    "wan": "󱥳",
    "waso": "󱥴",
    "wawa": "󱥵",
    "weka": "󱥶",
    "wile": "󱥷",
    "namako": "󱥸",
    "monsuta": "󱥽",
    "majuna": "󱦢",
    "leko": "󱥼",
    "soko": "󱦁",
    "kijetesantakalu": "󱦀" #... tonsi li lanpan ala lanpan e soko? :)
}

WRAP_LEFT = "󱦐"
WRAP_RIGHT = "󱦑"

WRAP_WORDS = {"manka", "aniso", "omniblock", "gui", "opengl"} # words to wrap in cartouches
WRAP_SET = {w.casefold() for w in WRAP_WORDS}

def tokenize(s):
    tokens = []
    buf = []

    def flush():
        if buf:
            tokens.append(("WORD", "".join(buf)))
            buf.clear()

    for ch in s:
        if ch.isalnum():
            buf.append(ch)
        else:
            flush()
            if ch.isspace():
                tokens.append(("SPACE", ch))
            else:
                tokens.append(("PUNCT", ch))

    flush()
    return tokens

def transform_words(tokens):
    out = []

    for t, v in tokens:
        if t != "WORD":
            out.append((t, v))
            continue

        raw = v
        key = v.casefold()

        if key in WRAP_SET:
            raw = f"{WRAP_LEFT}{raw}{WRAP_RIGHT}"

        out.append(("WORD", REPLACEMENTS.get(key, raw)))

    return out

def transform_punct(tokens):
    out = []
    i = 0

    while i < len(tokens):
        t, v = tokens[i]

        if t == "PUNCT" and v == ".":
            j = i + 1
            has_word = any(x[0] == "WORD" for x in tokens[j:])

            if has_word:
                out.append(("PUNCT", "󱦜"))
            else:
                out.append((t, v))

            i += 1
            continue

        if t == "PUNCT" and v in {"?", "!"}:
            glyph = "󱦚" if v == "?" else "󱤀"

            if out and out[-1][1] == glyph:
                i += 1
                continue

            out.append(("PUNCT", glyph))
            i += 1
            continue

        if t == "PUNCT" and v in REPLACEMENTS:
            out.append(("PUNCT", REPLACEMENTS[v]))
            i += 1
            continue

        out.append((t, v))
        i += 1

    return out

def untokenize(tokens):
    return "".join(v for t, v in tokens if t != "SPACE")

def transform(tokens):
    tokens = transform_words(tokens)
    tokens = transform_punct(tokens)
    return untokenize(tokens)

def process_json(obj):
    if isinstance(obj, dict):
        return {k: process_json(v) for k, v in obj.items()}
    if isinstance(obj, list):
        return [process_json(x) for x in obj]
    if isinstance(obj, str):
        return transform(tokenize(obj))
    return obj

with open("tok.json", "r", encoding="utf-8-sig") as f:
    data = json.load(f)

result = process_json(data)
result["lang"]["name"] = "󱥬󱥔 (󱥠󱥔)"
result["lang"]["font"] = "sevenish"

with open("sit.json", "w", encoding="utf-8") as f:
    json.dump(result, f, indent=2, ensure_ascii=False)
