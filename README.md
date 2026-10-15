# FourSquare VR — Sprint 2

## Project Overview

FourSquare VR is a multiplayer virtual reality recreation of the traditional Four Square playground game, developed for Meta Quest using Unity. The game allows four players to compete on a virtual Four Square court, hitting a ball between squares while avoiding elimination. Players aim to advance through the squares and ultimately reach the highest-ranking position, known as the King.

The goal of this project is to create an immersive multiplayer VR experience where players can interact naturally using VR controllers, create or join multiplayer rooms, and compete with other players in a shared virtual environment.

The project is developed using **Unity 6.3 LTS**, **XR Interaction Toolkit**, **Photon Fusion**, and **Photon Vision**, targeting **Meta Quest 3 / Android**.

## Getting Started

Open the project in Unity and load:

`Assets/Scenes/StartScene.unity`

The Start Scene contains the VR start-screen interface, multiplayer room creation and joining functionality, and the player lobby.

For headset testing, connect a Meta Quest device with Developer Mode and USB debugging enabled, switch the Unity build platform to Android, ensure `StartScene` is the first enabled scene, and use **Build and Run**.

## Sprint 2 Overview

During Sprint 2, our team focused on expanding the foundation established in Sprint 1 by integrating multiplayer networking, improving VR interactions, and implementing the room management system.

A major milestone was installing and configuring Photon Fusion and Vision to support multiplayer functionality. We also developed scripts for player elimination and teleportation, allowing players to teleport when eliminated. Currently, this behavior is triggered by holding a controller trigger as a temporary testing mechanism.

Additionally, we implemented room-code generation, multiplayer room creation and joining, a four-player lobby interface, and an interactive VR keyboard. These improvements establish the foundation for connecting multiple players within the same virtual Four Square environment.

## Photon Fusion and Networking

Sprint 2 introduced Photon Fusion to support multiplayer communication and session management.

The networking setup allows players to create and join multiplayer sessions using room codes. This provides the foundation for synchronizing players and gameplay interactions across different devices.

Photon Vision was also installed and configured as part of the project's networking-related development.

## Room Creation and Four-Letter Codes

Players can select **Create Room** from the start screen to generate a random four-letter room code.

The room-code system includes:

- Random four-letter room-code generation.
- Local tracking of generated codes to avoid duplicates during the current session.
- Displaying the generated room code in the Create Room interface.
- Integration of room-code generation with the Create Room button.
- Connecting generated room codes to Photon Fusion multiplayer sessions.

The local code-tracking system prevents duplicates within the current game instance. Additional validation is needed to guarantee uniqueness across all active multiplayer sessions.

## Join Room Functionality

Players can select **Join Room** from the start screen and enter a four-letter room code to connect to an existing multiplayer session.

The Join Room system includes room-code validation, Photon Fusion session joining, and navigation between the Start Screen and Join Room interface.

This functionality allows multiple players to connect to the same room using a shared code.

## Multiplayer Lobby

A multiplayer lobby was implemented to display the current room code, connected player count, and available player slots.

The lobby supports a maximum configured capacity of four players.

For example:

**ROOM CODE: ABCD**

**PLAYERS: 2/4**

- Player 1 — Connected
- Player 2 — Connected
- Waiting for Player 3...
- Waiting for Player 4...

The lobby updates the player count as participants connect or disconnect, allowing players to see how many positions remain available.

## VR Keyboard and User Interface Improvements

To support room-code entry in virtual reality, we integrated the XR Interaction Toolkit Spatial Keyboard.

Players can use their Quest controllers to select letters and enter room codes directly within the VR environment.

We also improved the user interface by adjusting the keyboard position, aligning the Join and Back buttons, and ensuring that the keyboard appears when the room-code input field is selected.

Additional UI improvements included organizing the multiplayer lobby, displaying connected-player information, and providing navigation between the different room screens.

## Leave Room and Session Management

The **Leave Room** functionality allows players to disconnect from the current Photon Fusion session and return to the Start Screen.

When the host leaves, the hosted multiplayer session shuts down, preventing other players from joining the closed room.

Further verification is needed for reusing released room codes and handling unexpected disconnections.

## Player Elimination and Teleportation

Sprint 2 also included development of scripts to support player elimination and teleportation during gameplay.

When a player is eliminated, the system is designed to teleport them to an appropriate location in the game environment.

Currently, the elimination and teleportation behavior uses holding a controller trigger as a temporary testing mechanism. This functionality is set up with networking and will be integrated with the actual Four Square elimination rules as gameplay development progresses.

## Multiplayer Testing

The multiplayer room system was tested using a Meta Quest headset as Player 1 and the Unity Editor on Mac as Player 2.

Testing confirmed the following:

- Successfully created a multiplayer room on Meta Quest.
- Successfully joined the same room from Mac using the generated four-letter code.
- Verified that the lobby updated from 1/4 to 2/4 connected players.
- Verified that the Leave Room button returned the player to the Start Screen.
- Verified that attempting to join the previous room failed after the host closed it.

These tests demonstrated successful multiplayer room creation, joining, player-count updates, and session disconnection using Photon Fusion.

## Sprint 2 Progress

During Sprint 2, the team made significant progress toward a fully functional multiplayer FourSquare VR experience. We installed and configured Photon Fusion and Vision, developed networked elimination and teleportation scripts, implemented four-letter room-code generation, and connected room creation and joining to Photon Fusion sessions.

We also introduced a multiplayer lobby supporting four player slots, integrated an XR Spatial Keyboard for VR text input, improved the user interface, and implemented Leave Room functionality.

Together, these features expand the initial VR gameplay foundation into a connected multiplayer experience.

## In Progress

Implementing multiplayer ball networking using Photon Fusion. The current work includes authoritative ball spawning, network synchronization of ball movement, and networked hit handling so player interactions can update the ball across connected clients. A NetworkBallSpawner, Fusion bootstrap configuration, and VR auto-start support have been added to the BallTest scene.

Remaining work: Verify prefab assignments and network configuration, test ball spawning and hit synchronization across multiple clients, and validate hit authority and collision behavior in multiplayer sessions.

## Current Development

The intended gameplay flow is:

**Start Screen → Create/Join Room → Multiplayer Lobby → Four Square Game → End Screen**

Room creation, joining, lobby updates, and leaving multiplayer sessions have been implemented and tested. Full gameplay integration, including synchronized player elimination and ball interactions, remains under development.

## Next Steps

Future development will focus on completing the multiplayer gameplay loop, integrating elimination and teleportation with the actual Four Square rules, synchronizing ball interactions across players, verifying the four-player capacity, improving room-code lifecycle management, and connecting the lobby to the gameplay and end-screen systems.

## Note on AI Usage

Our team used GitHub Copilot in Visual Studio Code as a development aid throughout the project. We used it to help generate and explain code, troubleshoot errors, and explore potential implementations. All AI-generated suggestions were reviewed and tested by team members, who remained responsible for understanding, integrating, and validating the code within the project.

## Team Members

| Team Member | Major(s) | Relevant Skills | Responsibilities |
|---|---|---|---|
| Helen Wu | CS/Math | 3D Modeling, Figma, Full-stack Development, Godot | Game assets, backend development, Git repository management |
| Alfred Tapia | CS | VR, 3D Modeling, Git, Full-stack Development | Game assets, networking, backend development |
| Gwen Goetz | Vocal Performance | Git, Backend Development, Sound Design | Story/task management, backend development, networking |
| Sreepriya Damuluru | CS | Full-stack Development, Git | Sprint updates, backend development, multiplayer room creation, joining, and lobby implementation |
