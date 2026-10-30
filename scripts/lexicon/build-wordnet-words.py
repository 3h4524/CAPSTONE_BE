"""Builds wordnet-words.json, the English word knowledge ArtworkPromptRules.SplitKeywords sorts keywords with.

The app needs no Python at run time: the file this writes is committed and embedded in APCS.Application. Run this only
to rebuild it (after changing the rules below):

    python -m pip install nltk
    python -m nltk.downloader wordnet
    python scripts/lexicon/build-wordnet-words.py

It writes two lists from Princeton WordNet 3.0:
- "family": words for a relative (mom, granddaughter, stepson ...), with their plurals. A keyword such as "dog mom" names
  who the product is for, and an image model asked for it writes the word out or draws a stranger.
- "adjectives": words used mostly as adjectives (geometric, coastal, whimsical ...). A keyword made only of them says
  how the design feels, not what is in it, so it becomes the mood instead of a motif.

Words about the listing itself (shirt, mug, gift) are the app's own vocabulary, not English knowledge, and live in
artwork-vocabulary.json, which is edited by hand.
"""
import json
import os
import re

from nltk.corpus import wordnet as wn

OUTPUT = os.path.join(os.path.dirname(__file__), "..", "..", "Application", "Features", "BatchProductPrompts",
                      "Common", "Vocabulary", "wordnet-words.json")

# Relatives a design draws rather than sells to ("baby elephant", "cherub"), or words whose everyday meaning is not a
# relative at all ("pop art", "quad bike", "twin peaks").
NOT_FAMILY = {
    "babe", "baby", "cherub", "infant", "neonate", "newborn", "nurseling", "nursling", "papoose", "pappoose", "preemie",
    "premie", "pop", "quad", "quin", "twin", "simeon",
}

PLAIN_WORD = re.compile(r"^[a-z]+$")
RELATIVE = wn.synset("relative.n.01")


def tagged_count(synset, word):
    """How often the word was seen in this sense in WordNet's tagged texts."""
    return sum(lemma.count() for lemma in synset.lemmas() if lemma.name().lower() == word)


def main_noun_sense(word):
    """The word's most common noun sense; WordNet's own order breaks ties."""
    senses = wn.synsets(word, wn.NOUN)
    if not senses:
        return None
    return max(senses, key=lambda sense: (tagged_count(sense, word), -senses.index(sense)))


def is_family(word):
    sense = main_noun_sense(word)
    return sense is not None and (sense == RELATIVE or RELATIVE in sense.closure(lambda s: s.hypernyms()))


def plurals(word):
    """The word's plurals: the regular English one, and any irregular one WordNet lists (brethren, godchildren)."""
    found = {form for form, bases in wn._exception_map[wn.NOUN].items() if word in bases and PLAIN_WORD.match(form)}
    if word.endswith("s"):
        return found  # gramps, missus: already a plural, or one nobody makes
    if word.endswith("man"):
        found.add(word[:-3] + "men")
    elif word.endswith("wife"):
        found.add(word[:-4] + "wives")
    elif word.endswith("child"):
        found.add(word + "ren")
    elif re.search(r"(x|z|ch|sh)$", word):
        found.add(word + "es")
    elif re.search(r"[^aeiou]y$", word):
        found.add(word[:-1] + "ies")
    else:
        found.add(word + "s")
    return found


def is_mostly_adjective(word):
    """True when the word's adjective senses were seen more often than its noun or verb ones (a noun wins a tie)."""
    totals = {}
    for pos in (wn.NOUN, wn.VERB, wn.ADJ):
        senses = wn.synsets(word, pos)
        if senses:
            totals[pos] = sum(tagged_count(s, word) for s in senses) + (0.5 if pos == wn.NOUN else 0)
    return bool(totals) and max(totals, key=totals.get) == wn.ADJ


def build():
    family = set()
    for synset in RELATIVE.closure(lambda s: s.hyponyms()):
        for lemma in synset.lemmas():
            word = lemma.name().lower()
            if PLAIN_WORD.match(word) and word not in NOT_FAMILY and is_family(word):
                family.add(word)
                family |= plurals(word)

    adjectives = {
        lemma.name().lower()
        for synset in wn.all_synsets(wn.ADJ)
        for lemma in synset.lemmas()
        if PLAIN_WORD.match(lemma.name().lower())
    }
    adjectives = {word for word in adjectives if is_mostly_adjective(word)}

    license_text = wn.license().strip()
    return {
        "source": "Princeton WordNet 3.0, read through NLTK by scripts/lexicon/build-wordnet-words.py.",
        "copyright": "WordNet 3.0 Copyright 2006 by Princeton University. All rights reserved.",
        "license": license_text,
        "family": sorted(family),
        "adjectives": sorted(adjectives),
    }


if __name__ == "__main__":
    words = build()
    with open(OUTPUT, "w", encoding="utf-8", newline="\n") as file:
        json.dump(words, file, ensure_ascii=False, indent=1)
        file.write("\n")
    print(f"{len(words['family'])} family words, {len(words['adjectives'])} adjectives -> {os.path.normpath(OUTPUT)}")
