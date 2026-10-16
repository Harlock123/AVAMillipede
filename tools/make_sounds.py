#!/usr/bin/env python3
"""Synthesises AVAMillipede's sound effects and tunes into ../src/AVAMillipede/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAMillipede", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The arrow's twang, and what it hits: a mushroom's thock, a body segment's pop, a head's louder one.
    write("Arrow.wav", tone(0.08, 1500, 2600, "square", release=0.6), 0.14)
    write("Mushroom.wav", tone(0.05, 300, 220, "square", release=0.7), 0.18)
    write("Segment.wav", tone(0.1, 700, 300, "square", release=0.6), 0.24)
    write("Head.wav", mix(tone(0.18, 900, 200, "square", release=0.6), tone(0.12, 2000, 800, "noise", release=0.7, gain=0.4)), 0.3)
    write("Kill.wav", mix(tone(0.3, 1100, 150, "noise", release=0.5), tone(0.25, 600, 120, "square", release=0.6, gain=0.5)), 0.32)
    # DDT going off: a long hissing whoomp.
    write("Ddt.wav", mix(tone(1.0, 2500, 300, "noise", attack=0.02, release=0.4), tone(0.6, 120, 50, "saw", release=0.5, gain=0.6)), 0.42)
    # The bugs arriving: the spider's skitter, the earwig's chitter; the beetle shot, the inchworm's slow-down.
    write("Spider.wav", cat(*[tone(0.05, f, f, "pulse", release=0.6) for f in [700, 900, 700, 900, 700, 900]]), 0.18)
    write("Earwig.wav", tone(0.4, 1800, 1200, "square", release=0.5, vibrato=0.2, vib_rate=30), 0.16)
    write("Beetle.wav", cat(tone(0.15, 200, 120, "square", release=0.5), tone(0.2, 150, 80, "square", release=0.5)), 0.3)
    write("Inchworm.wav", tone(0.8, 900, 200, "tri", release=0.3, vibrato=0.05, vib_rate=4), 0.3)
    write("Swarm.wav", tone(0.12, 1200, 1800, "pulse", release=0.6), 0.22)
    write("Death.wav", cat(mix(tone(0.6, 1200, 60, "noise", release=0.4), tone(0.5, 600, 50, "saw", release=0.4, gain=0.6)), *[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["C5", "A4", "F4", "D4"]]), 0.46)
    write("Restore.wav", tone(0.05, 1600, 2000, "pulse", release=0.6), 0.16)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["D5", "F#5", "A5", "D6"]]), 0.32)
    write("WaveClear.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["A5", "D6", "F#6", "A6", "D7"]]), 0.36)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["A5", "E6", "A5", "E6", "A6"]]), 0.38)

    # The millipede's beat: a creeping two-note bass with a scurrying figure above (whole bars at 140 bpm).
    bass = [(n, 0.5) for n in ["D2", "A2"] * 4 + ["C2", "G2"] * 4 + ["Bb1", "F2"] * 4 + ["A1", "E2"] * 4]
    lead = [("D5", 0.5), ("E5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 2), ("C5", 0.5), ("D5", 0.5), ("E5", 0.5), ("D5", 0.5), ("C5", 2),
            ("Bb4", 0.5), ("C5", 0.5), ("D5", 0.5), ("C5", 0.5), ("Bb4", 2), ("A4", 0.5), ("C#5", 0.5), ("E5", 0.5), ("A5", 0.5), ("-", 2)]
    write("Music.wav", tune(140, lead, bass, kind="tri"), 0.18)

    # The title: a bustling garden tune in D (an original composition).
    lead = [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("F#5", 0.5), ("G5", 0.5), ("B5", 0.5), ("A5", 1), ("F#5", 0.5), ("D5", 0.5), ("E5", 0.5), ("F#5", 0.5), ("E5", 2),
            ("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 0.5), ("C#6", 0.5), ("B5", 0.5), ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("C#5", 0.5), ("E5", 0.5), ("D5", 2)]
    bass = [("D3", 1), ("A2", 1), ("G2", 1), ("A2", 1), ("D3", 1), ("B2", 1), ("A2", 2),
            ("D3", 1), ("A2", 1), ("G2", 1), ("E2", 1), ("A2", 1), ("A2", 1), ("D3", 2)]
    write("TitleTune.wav", cat(tune(144, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(160, [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 0.5), ("A5", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(72, [("A4", 1), ("F4", 1), ("E4", 1), ("C#4", 1), ("D4", 4)], [("D2", 2), ("Bb1", 2), ("A1", 2), ("D2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
