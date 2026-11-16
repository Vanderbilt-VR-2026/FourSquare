# FourSquare VR — Sprint 4

## 1. Project Overview

FourSquare VR is a multiplayer virtual reality recreation of the traditional Four Square playground game, developed using Unity for Meta Quest headsets.

The game allows up to four players to compete on a virtual Four Square court. Players hit the ball between squares, avoid gameplay faults, and attempt to advance through the rankings to reach the highest position, known as King.

The project combines VR interactions, Photon Fusion multiplayer networking, voice communication, physics-based gameplay, and a stylized virtual city environment.

### Gameplay Flow

**Start Screen → Create/Join Room → Multiplayer Lobby → City Gameplay → End Screen**

---

## 2. Sprint 4 Overview

During Sprint 4, the team focused on improving multiplayer communication, implementing game rules, refining the scoring and ranking system, and enhancing player interactions.

Building on the visual, audio, and gameplay improvements from Sprint 3, this sprint focused on creating a more complete multiplayer gameplay experience.

### Major Development Areas

- Photon Voice integration for multiplayer communication.
- Game rules and player ranking structure.
- Ball and court interactions.
- Player teleportation to assigned court positions.
- Faster multiplayer lobby creation.
- Displaying lobby codes in the City scene.
- Ball color changes during gameplay.
- Improvements to the Start Game interaction.
- Start Screen UI interaction improvements.
- Player tagging and avatar-height preparation.
- End Screen UI improvements.
- Smoother Start Screen transitions.

---

## 3. Sprint 4 Implementations

### 3.1 Multiplayer Voice Chat

Photon Voice 2 was integrated with the existing Photon Fusion multiplayer system to allow players to communicate during gameplay.

The implementation includes:

- Adding `FusionVoiceClient` to the existing `NetworkRunner`.
- Registering Photon Voice callbacks for multiplayer sessions.
- Configuring a separate Photon Voice App ID.
- Adding `VoiceNetworkObject` to the networked VR Player prefab.
- Configuring Recorder, Speaker, and AudioSource components.
- Enabling microphone access for Meta Quest.
- Connecting players to a voice room associated with their multiplayer session.



### 3.2 Game Rules Implementation

The team continued developing and integrating gameplay rules to create a more structured Four Square experience.

The gameplay rules cover:

- Keeping the ball within the court boundaries.
- Detecting multiple bounces.
- Identifying gameplay faults.
- Supporting player elimination and ranking changes.
- Managing court positions during gameplay.

A How To Play panel was also developed to explain the basic game instructions.



### 3.3 Score Ranking Structure

A structure for tracking player rankings was added to support the Four Square gameplay system.

The ranking system is intended to organize players according to their assigned court positions and update rankings as gameplay progresses.

The goal is to allow players to advance toward the King position while accounting for gameplay faults and eliminations.


### 3.4 Ball and Court Teleportation

The team connected ball and court behavior with player teleportation to support movement between assigned court positions.

This work supports positioning players in the appropriate areas of the Four Square court as gameplay progresses.

### 3.5 Instant Multiplayer Lobby Creation

Multiplayer lobby creation was improved to reduce the waiting time when players select Create Room.

The goal was to make room creation feel faster and more responsive.

This improvement builds on the existing Photon Fusion multiplayer system.

### 3.6 Lobby Code Display in City Scene

The multiplayer lobby code was made available in the City gameplay scene.

This allows the room identifier to remain accessible after players transition from the Start Screen to the gameplay environment.


### 3.7 Ball Color Changes

Ball color-changing behavior was added to the Four Square court gameplay.

This introduces additional visual feedback during ball interactions.


### 3.8 Start Game Interaction Fixes

The Start Game interaction in the join zone was updated to address previous interaction issues.

The team is considering whether the current implementation should be replaced with a different interaction approach.

### 3.9 Start Screen Button Interaction Improvements

The Start Screen UI was improved to make button clicking more responsive and reliable.

These improvements focus on interactions with menu controls, including room creation, joining, and navigation.

### 3.10 Player Tag Setup

The player-tagging structure was prepared for future avatar configuration.

The final setup will be completed after determining the appropriate avatar height.


### 3.11 End Scene UI Improvements

The End Scene interface was updated to better match the Start Screen design.

The goal is to maintain consistent visual styling across the game, including fonts, colors, buttons, and panel layouts.

### 3.12 Improved Start Screen Transitions

Screen transitions in StartScene were refined to make navigation between UI panels smoother.

These improvements support a more consistent experience when moving between the main menu, multiplayer options, and instructions.

## Sprint 4 Progress Summary

During Sprint 4, the FourSquare VR team focused on improving multiplayer communication, implementing game rules, enhancing gameplay mechanics, and refining the overall user experience.

One of the major accomplishments was integrating **Photon Voice Chat**, allowing players to communicate during multiplayer gameplay. The team also introduced a **score ranking structure**, improved game-rule functionality, and connected player teleportation with court assignments.

Multiplayer improvements included **faster lobby creation**, displaying room codes in the City scene, and improving the Start Game interaction. Additional gameplay enhancements included ball color changes and better coordination between player movement and court interactions.

The team also improved the **Start Screen UI**, including more responsive button interactions and smoother screen transitions. The **End Scene UI** was updated to maintain visual consistency with the Start Screen, and the player-tagging structure was prepared for future avatar-height adjustments.

Although significant progress was made, some issues remain, including the **ball reset bug**, adjusting player spawn points closer to the court, and ensuring consistent voice-chat functionality across different devices.

Overall, Sprint 4 brought FourSquare VR closer to a complete and immersive multiplayer experience. The next phase will focus on resolving remaining bugs, improving gameplay reliability, and conducting additional multiplayer testing on Meta Quest devices.

---

## Note on AI Usage

Our team used GitHub Copilot in Visual Studio Code as a development aid throughout the project.

AI tools were used to help generate and explain code, troubleshoot errors, and explore potential implementations.

All AI-generated suggestions were reviewed and tested by team members, who remained responsible for understanding, integrating, and validating the code within the project.

---

## Team Members

| Team Member | Major(s) | Relevant Skills | Responsibilities |
|---|---|---|---|
| Helen Wu | CS/Math | 3D Modeling, Figma, Full-stack Development, Godot | Game assets, backend development, Git repository management |
| Alfred Tapia | CS | VR, 3D Modeling, Git, Full-stack Development | Game assets, networking, backend development |
| Gwen Goetz | Vocal Performance | Git, Backend Development, Sound Design | Story/task management, backend development, networking |
| Sreepriya Damuluru | CS | Full-stack Development, Git | Sprint documentation, backend development, multiplayer room creation/joining, lobby implementation, audio management |

---

