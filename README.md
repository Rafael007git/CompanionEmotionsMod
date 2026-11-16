# Companion Emotions Mod (BepInEx)

A **BepInEx** plugin for *She Will Punish Them* that brings companions (and optionally the player character) to life through dynamic emotional states, physiological reactions, custom covering poses, and autonomous castle behavior.

---

## ✨ Key Features

### 1. Dynamic Embarrassment & Modesty (ENF System)
* **Context-Aware Clothing Detection:** Scans equipped armor, bras, panties, and full-body lingerie (`hidePanties` support) to determine top and bottom exposure.
* **Camera & Gaze Sensors:** Embarrassment scales dynamically when a character is unclothed, looked at by the camera within close range, and facing the observer.
* **Procedural Covering Poses (`enf_cover-*.bytes`):** When embarrassment passes the threshold (`55+`), characters smoothly blend into custom covering poses (`ES2` format).
  * **Movement-Aware Blending:** Arm covering remains active even while walking or running, while torso and leg adjustments smoothly blend in when standing still.
* **Arousal Dampener:** Higher arousal naturally suppresses embarrassment growth (up to `-75%` at peak arousal). (To be properly implemented into gameplay).

### 2. Physiological & Visual Reactions
* **Dynamic Cheek Blush:** Smoothly blends a custom facial blush decal (`face_blush.png`) across face, lip, and ear materials proportional to current embarrassment (automatically yields if a custom face tattoo is equipped).
* **Skin Sheen / Sweat:** Dynamically increases skin smoothness (`_SmoothnessDeviate`) under emotional stress or arousal, with a configurable minimum peak threshold so dry skin presets still show a subtle sheen.
* **Procedural Breathing:** Modulates chest and stomach blendshapes in real time, accelerating breathing frequency and depth as emotional intensity rises.

### 3. Living Castle AI & Music Performances
* **Autonomous Castle Wandering:** Replaces buggy vanilla furniture teleportation with real NavMesh pathfinding—companions walk between rooms, explore halls, and naturally interact with castle furniture.
  * **Diagnostic Overlay (`F8`):** Optional real-time debug box showing current Embarrassment, Arousal, clothing states, camera gaze checks, and Castle AI pathfinding status.
  * **Free Pose Freeze Fix:** Restores companion physics, animator speed, and NavMesh navigation cleanly upon exiting Free Pose Mode.
* **Smart Musical Sync:** When a companion uses a musical instrument or performance furniture with attached 3D audio (via *Furniture Animations Mod*), she will stay and finish playing the current track before moving on to her next activity. (Requires **Furniture Animations** mod with configured animations and audio tracks)

---

## 📦 Requirements & Dependencies

* **BepInEx 5.x**
* **Furniture Animations Mod** (`com.lorifel007.furnitureanimations`) — **Required** for autonomous furniture pose playback and audio track synchronization.
* **Tattoos Mod** (`aedenthorn.Tattoos`) — *Optional (Soft Dependency).*

---

## 🛠️ Installation

1. Install **BepInEx 5.x** and **Furniture Animations Mod**.
2. Extract the mod files into your `BepInEx\plugins\` directory:
   ```text
   BepInEx/
   └── plugins/
       ├── CompanionEmotionsMod.dll
       └── CompanionEmotionsMod/
           ├── textures/
           │   └── face_blush.png
           └── poses/
               ├── enf_cover-01.bytes
               └── enf_cover-02.bytes
   ```
3. Launch the game once to generate `BepInEx\config\com.lorifel007.companionemotions.cfg`.

---

## ⚙️ Configuration (`com.lorifel007.companionemotions.cfg`)

* **General Settings**
  * `Include Player Character` *(Default: `true`)* — Enable emotional reactions, physiology, and ENF poses for the player character as well as companions.
  * `Enable Castle Life Fix` *(Default: `true`)* — Enables autonomous NavMesh castle wandering and fixes post-Free-Pose animation freezes.
* **Castle AI Settings**
  * `Min Routine Interval` / `Max Routine Interval` *(Default: `60s` – `180s`)* — Time range before a companion picks a new destination or furniture piece.
* **Balance & Visual Settings**
  * `Base Gain Speed` / `Base Decay Speed` *(Default: `25` / `15`)* — Rate of embarrassment buildup and recovery per second.
  * `Blush Color` / `Max Blush Intensity` *(Default: `0.95`)* — Tint and maximum opacity of the dynamic blush overlay.
  * `Sweat Gloss Boost` / `Min Peak Sweat Gloss` *(Default: `0.30` / `0.12`)* — Controls skin sheen intensity at peak emotional stress.
* **Debug Settings**
  * `Show Debug Overlay` *(Default: `false`)* / `Toggle Overlay Key` *(Default: `F8`)* — Toggles the floating diagnostic HUD above characters.

---

## 🗺️ To-Do & Roadmap

### Short-Term / Polishing
- [ ] **Bug: twitching during animation** When countdown reaches zero and companion wants to leave the furniture her animation may start twitching. We should not start her Navigation system unless the animation has stopped.
- [ ] **Enhanced Stuck Recovery:** Improve narrow-corridor NavMesh obstacle avoidance and soft unstuck teleportation for companions.
- [ ] **Weighted Furniture Selection:** Support per-pose probability weights in furniture configs so companions favor signature animations (e.g., playing instruments) over idle poses.
- [ ] **Persistent Emotion Memory:** Save and load companion emotional baselines and traits across game sessions.

### Gameplay & Multi-Factor Expansion
- [ ] **Inspiration & Group Scenes:** Broadcast nearby music playback so idle companions gain *Inspiration*, gather around performers, dance, or use easels and posing spots.
- [ ] **Arousal & Pleasure System:** Expand the Arousal scale with environmental triggers, caresses, toys, paired animations, and climax reactions.
- [ ] **Pain & Personality Traits:** Add pain/stimulation responses (spanking, whips, paired scenes) alongside personality traits (e.g., *Shy*, *Exhibitionist*, *Submissive*, *Masochist*) that dynamically alter emotional thresholds and pose choices.
- [ ] **Cross-Mod Roleplay Hooks:** Expose public API hooks for *CompanionRoleplay* so carrying, leading, or restraining companions dynamically influences embarrassment, trust, and arousal.
