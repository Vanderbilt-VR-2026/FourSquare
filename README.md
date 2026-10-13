
# FourSquare VR — Sprint 1

FourSquare VR is a virtual reality recreation of the traditional Four Square game, developed for Meta Quest using Unity. The goal of the project is to create an interactive multiplayer VR experience where players can enter a Four Square court, interact with the ball using VR controllers, and create or join rooms to play with other players.

The project is developed using **Unity 6.3 LTS (6000.3.23f1)** with the **XR Interaction Toolkit** and is currently targeting **Meta Quest 3 / Android**.

## Getting Started

Open the project in Unity and load:

`Assets/Scenes/MainScene.unity`

The Main Scene contains the current VR player setup, Four Square environment, start-screen interface, game-state management, and gameplay components.

For headset testing, connect a Meta Quest device with Developer Mode and USB debugging enabled, switch the Unity build platform to Android, and use **Build and Run**.

## VR Controls and Interaction

The player uses the Quest controllers to interact with the VR environment.

Controller interactions currently include pointing at and selecting world-space UI elements and interacting with gameplay objects. The project also includes the foundation for ball interaction and physics-based Four Square gameplay.

Unnecessary locomotion actions such as jumping and joystick-based movement/rotation have been removed from the current player setup so that players remain oriented toward the Four Square court during gameplay.

## Start Screen and Room Creation

Sprint 1 introduces a VR start-screen interface with the following options:

- **Create Room**
- **Join Room**
- **Quit**

The Create Room and Join Room interfaces include Back navigation to return to the main start screen.

Selecting **Create Room** generates a random four-letter room code, such as `FRJE`. Generated codes are checked against codes already generated during the current session to prevent local duplicates.

The current room-code implementation is local. Multiplayer room registration and validation across different Quest devices will be integrated with the networking system in a later sprint.

## Game State and Environment

Sprint 1 establishes the initial architecture required for Four Square gameplay.

The project includes a `GameStateManager` and supporting game-state types for tracking gameplay information. Graybox environments and the Four Square court have also been created to provide the foundation for gameplay development and testing.

Ball hit, bounce, and physics mechanics have been developed as part of the initial gameplay interaction system.

## Sprint 1 Progress

During Sprint 1, the team focused on building the core VR foundation required for future multiplayer gameplay. Work completed during the sprint includes the initial VR scene and controller input, VR start-screen UI, Create Room and Join Room navigation, game-state management, graybox environments and Four Square court development, and initial ball hit and bounce mechanics.

Additional Sprint 1 work includes integrating the gameplay ball into the main scene, developing the end-screen UI as a separate scene, and implementing four-letter room-code generation for the Create Room workflow.

## Current Development

The project currently provides the foundation for the complete Four Square gameplay loop:

`Start Screen → Create/Join Room → Four Square Game → End Screen`

Some parts of this flow are still under development and integration. In particular, generated room codes are not yet connected to a shared multiplayer room registry.

## Next Steps

Future development will focus on connecting room creation and room-code entry to the multiplayer networking system, allowing multiple Quest users to join the same room, completing the player lobby flow, integrating the start/game/end scenes, and continuing development of the full Four Square gameplay experience.



| Team Member   | Major(s) | Relevant Skills | Responsibilities |
| -------- | ------- | ------- | ------- | 
| Helen Wu  | CS/Math | 3D modeling, Figma, Full-stack Development, Godot | Develop game assets, Backend development, Git repo manager |
| Alfred Tapia | CS | VR, 3D modeling, Git, Full-stack Development | Develop game assets, Networking, Backend development |
| Gwen Goetz | Vocal Performance | Git, Backend Development, Sound Design | Story/task management, Backend development, Networking |
| Sreepriya Damuluru | CS | Full-stack Development, Git,  | Spring update submissions, Backend development |

Four Square is a multiplayer playground game where four players are attempting to reach the highest level square (King). Players will be hitting a ball back and forth across the court, and they are avoiding elimination from missing the ball or hitting it out. 
