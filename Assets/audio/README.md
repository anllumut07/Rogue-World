# Rogue World - Audio Assets

## Directory Structure

```
Assets/audio/
├── music/          # Background music tracks (OGG format, loopable)
├── sfx/            # Sound effects (OGG/WAV format, short)
└── ambient/        # Ambient loops (OGG format, seamless)
```

---

## Required Music Tracks (CC0 / Public Domain / Custom)

| File | Description | Duration | Loop |
|------|-------------|----------|------|
| `menu_main.ogg` | Main menu theme | 2-3 min | Yes |
| `game_peace.ogg` | Peaceful gameplay | 3-5 min | Yes |
| `game_tension.ogg` | Rising tension/war prep | 2-3 min | Yes |
| `game_war.ogg` | Active warfare | 2-3 min | Yes |
| `game_crisis.ogg` | Major crisis/disaster | 1-2 min | Yes |
| `victory.ogg` | Victory screen | 30-60 sec | No |
| `defeat.ogg` | Defeat/game over | 30-60 sec | No |

**Technical Specs:**
- Format: OGG Vorbis (quality 5-6, ~128-192 kbps)
- Sample Rate: 44.1 kHz or 48 kHz
- Channels: Stereo
- Loop points: Seamless (end matches start)
- Loudness: -14 to -16 LUFS (streaming standard)

**Sources (CC0/Public Domain):**
- https://opengameart.org (search "ambient", "strategy", "orchestral")
- https://freesound.org (filter: CC0, tag: "music", "loop")
- https://incompetech.com (Kevin MacLeod - CC BY, credit required)
- https://www.bensound.com (royalty-free with attribution)
- Create custom with: LMMS, GarageBand, Reaper, or AI tools (Udio, Suno)

---

## Required Sound Effects (SFX)

| File | Description | Duration | Trigger |
|------|-------------|----------|---------|
| `ui_click.ogg` | Button click | 0.1-0.2s | Any button press |
| `ui_hover.ogg` | Button hover | 0.05-0.1s | Mouse enter button |
| `ui_open_panel.ogg` | Panel open | 0.2-0.3s | Panel opens |
| `ui_close_panel.ogg` | Panel close | 0.1-0.2s | Panel closes |
| `ui_tab_switch.ogg` | Tab change | 0.1-0.2s | TabBar selection |
| `ui_error.ogg` | Invalid action | 0.2-0.3s | Failed action |
| `ui_success.ogg` | Action complete | 0.2-0.3s | Research complete, build done |
| `notification.ogg` | Event popup | 0.3-0.5s | Important event |
| `war_declare.ogg` | War declaration | 0.5-1.0s | Declare war |
| `peace_sign.ogg` | Peace treaty | 0.5-1.0s | Sign peace |
| `election_win.ogg` | Election victory | 1-2s | Win election |
| `election_lose.ogg` | Election defeat | 1-2s | Lose election |
| `disaster_strike.ogg` | Disaster occurs | 1-2s | Disaster event |
| `nuclear_launch.ogg` | Nuclear strike | 2-3s | Nuclear use |
| `rocket_launch.ogg` | Space launch | 2-3s | Space mission |
| `construction_complete.ogg` | Building done | 0.3-0.5s | Factory complete |
| `research_complete.ogg` | Tech finished | 0.5-1.0s | Research done |
| `focus_complete.ogg` | Focus finished | 0.5-1.0s | Focus complete |
| `level_up.ogg` | Character level up | 0.5-1.0s | Attribute increase |
| `money_gain.ogg` | Income received | 0.2-0.3s | Daily/weekly income |
| `money_loss.ogg` | Expense paid | 0.2-0.3s | Construction cost |

**Technical Specs:**
- Format: OGG Vorbis (quality 4-5) or WAV (for very short <0.5s)
- Sample Rate: 44.1 kHz
- Channels: Mono (for 3D positioning) or Stereo
- Loudness: -18 to -20 LUFS (so they sit under music)

**Sources (CC0):**
- https://opengameart.org (search "ui", "button", "interface")
- https://freesound.org (filter: CC0, tag: "ui", "click", "notification")
- https://kenney.nl/assets (UI Audio packs - CC0)
- Record custom: finger snaps, desk taps, paper shuffles

---

## Required Ambient Loops

| File | Description | Duration | Use Case |
|------|-------------|----------|----------|
| `map_ambient.ogg` | Subtle map noise | 30-60s | World map background |
| `war_room.ogg` | Distant radio/chatter | 60-120s | During war |
| `peaceful_capital.ogg` | City ambience | 60-120s | Peacetime |
| `crisis_tension.ogg` | Low hum, sirens distant | 30-60s | High tension |

**Technical Specs:**
- Format: OGG Vorbis (quality 5)
- Sample Rate: 44.1 kHz
- Channels: Stereo
- Loop: Perfectly seamless
- Volume: Very low (-30 to -40 LUFS)

---

## Integration Notes

### Current Implementation (AudioSystem.cs)
The game currently uses **procedural audio** via `AudioStreamGenerator`:
- Sine wave tones for music (menu/game)
- Short sine bursts for SFX (click, war, peace, event)
- No external files required

### To Add Real Audio Files:

1. **Place files** in appropriate subdirectories above
2. **Modify `AudioSystem.cs`** to load `AudioStreamOggVorbis` instead of generating:
   ```csharp
   // Instead of PlayToneBuffer:
   var stream = ResourceLoader.Load<AudioStreamOggVorbis>("res://Assets/audio/music/menu_main.ogg");
   _musicPlayer.Stream = stream;
   _musicPlayer.Play();
   ```
3. **Add to `SettingsSystem`** for volume control per category (music/sfx/ambient)
4. **Test in headless** — audio still works but won't be audible (that's fine)

### Godot Import Settings
- **Music/Ambient:** Loop = On, Compress = Vorbis, Quality = 0.5-0.6
- **SFX:** Loop = Off, Compress = Vorbis, Quality = 0.4-0.5 (or WAV for <0.5s)
- **All:** Force Max Size = 2048 (for mobile/low-end), but we're desktop-only so 4096 OK

---

## Licensing Checklist

For each audio file added:
- [ ] License verified (CC0 / Public Domain / CC BY with attribution / Custom)
- [ ] Attribution recorded in `CREDITS_AUDIO.md` (if required)
- [ ] Format converted to OGG Vorbis
- [ ] Loop points verified (music/ambient)
- [ ] Loudness normalized
- [ ] File named per convention above
- [ ] Tested in-game

---

## Attribution Template (CREDITS_AUDIO.md)

```markdown
# Audio Credits

## Music
- `menu_main.ogg` — "Track Name" by Artist (CC0) — Source: URL
- `game_peace.ogg` — "Track Name" by Artist (CC BY 4.0) — Source: URL — Attribution: "Music by Artist"

## Sound Effects
- `ui_click.ogg` — "Click Sound" by Artist (CC0) — Source: URL

## Ambient
- `map_ambient.ogg` — "Ambient Track" by Artist (CC0) — Source: URL
```

---

## Priority Order for v1.0

1. **UI clicks** (most heard) — `ui_click.ogg`, `ui_open_panel.ogg`, `ui_close_panel.ogg`
2. **Menu music** — `menu_main.ogg`
3. **Gameplay music** — `game_peace.ogg`, `game_tension.ogg`
4. **Key notifications** — `notification.ogg`, `war_declare.ogg`, `peace_sign.ogg`
5. **Completion sounds** — `research_complete.ogg`, `focus_complete.ogg`, `construction_complete.ogg`
6. **Ambient** — `map_ambient.ogg`
7. **Remaining SFX** — Lower priority, can use procedural fallback

---

## Testing Audio

```bash
# In Godot editor:
# 1. Open Project Settings → Audio → Bus Layout
# 2. Verify "Master" bus exists
# 3. Add "Music" and "SFX" buses if needed
# 4. Run game, open Audio tab in debug panel
# 5. Verify streams play, volumes work

# Headless test:
godot --headless --path . -- --screenshot-test
# Check log for "[Audio] Menü müziği çalıyor" etc.
```