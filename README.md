# FourSquare VR — Sprint 3

## Project Overview

FourSquare VR is a multiplayer virtual reality recreation of the traditional Four Square playground game, developed for Meta Quest using Unity. The game is designed to allow up to four players to compete on a virtual Four Square court, hitting a ball between squares while avoiding elimination.

Players aim to advance through the squares and ultimately reach the highest-ranking position, known as the King.

The goal of this project is to create an immersive and accessible multiplayer VR experience where players can interact naturally using VR controllers, create or join multiplayer rooms, and compete in a shared virtual environment.


The intended gameplay flow is:

**Start Screen → Create/Join Room → Multiplayer Lobby → City Gameplay → End Screen**

### Available Unity Scenes

| Scene | Purpose |
|---|---|
| `StartScene` | Start Screen, room creation, joining, and multiplayer lobby |
| `City` | Main outdoor Four Square gameplay environment |
| `BallTest` | Ball physics and interaction testing |
| `EndScene` | End-of-game interface |
| `Tartarus` | Additional scene developed during the project |
| `SampleScene` | Development/sample scene |

Some scenes and features remain under development and are not yet fully connected to the main gameplay flow.

---

## Sprint 3 Overview

During Sprint 3, the team focused on improving the visual design, audio experience, and ball interaction mechanics of FourSquare VR.

Building on the multiplayer foundation established during Sprint 2, this sprint introduced more intentional design decisions to make the game visually engaging, immersive, and easier to interact with.

The major development areas included:

1. Dynamic skyboxes that change based on the user's local time.
2. Additional City environment meshes, materials, and interface styling.
3. Background music and sound effects for menus and gameplay.
4. Improvements to ball-hit physics and controlled landing behavior.
5. Continued testing and refinement of the multiplayer VR experience.

These improvements aim to create a more cohesive game environment while supporting accessible and enjoyable gameplay.

---

## Sprint 3 Implementations

### 1. Dynamic Time-of-Day Skyboxes

A dynamic skybox system was introduced to make the game environment more responsive to the player's real-world surroundings.

The system checks the user's local time and selects an appropriate skybox material.

This functionality was applied to both:

- `StartScene`
- `City`

The skybox changes help establish different environmental moods throughout the day while maintaining a consistent visual style.

**Design Choice:** The skybox system was introduced to make the virtual environment feel more dynamic and connected to the player's real-world time rather than displaying the same background during every session.

**Screenshots:**

*Figure 1: Start Scene showing the dynamic skybox.*

[Insert Start Scene skybox screenshot]

*Figure 2: City environment showing a different time-of-day skybox.*

[Insert City skybox screenshot]

### 2. City Environment and Visual Improvements

Additional meshes were introduced into the City scene to make the environment feel more lively and visually complete.

The work included:

- Adding environmental meshes to improve the appearance of the City.
- Creating and assigning custom materials to meshes.
- Improving the overall visual consistency of the outdoor environment.
- Updating fonts used in Start Screen panels to better match the game's visual style.

The environment uses a stylized visual approach rather than attempting to recreate a fully realistic city.

**Design Choice:** The stylized environment supports a playful sports-game atmosphere. The updated materials, meshes, and fonts help create a more recognizable visual identity across the game.

**Screenshots:**

*Figure 3: City environment with additional meshes and custom materials.*

[Insert City environment screenshot]

*Figure 4: Updated Start Screen showing custom fonts and interface styling.*

[Insert Start Screen UI screenshot]

### 3. Audio Management and Sound Effects

An audio system was introduced to improve immersion and provide feedback during interactions.

Unity's Audio Mixer was configured with separate groups for background music and sound effects.

#### Audio Mixer

The audio system contains:

- **Master** — Overall audio output.
- **Music** — Background music for different scenes.
- **SFX** — Sound effects for UI interactions and gameplay.

Separating these groups allows music and sound effects to be adjusted independently.

#### Start Screen Background Music

Background music was added to `StartScene` to create a welcoming and playful atmosphere when players first enter the game.

The Start Screen music is configured to loop and play through the Music mixer group.

#### City Gameplay Music

A separate background music track was added to the City scene.

The gameplay music is intended to create a more energetic atmosphere during matches while remaining quieter than important gameplay sound effects.

#### UI Button Sound Effects

Button click sounds were integrated into the Start Screen interface.

These sounds provide feedback for interactions such as:

- Create Room
- Join Room
- Back
- Quit
- Room confirmation and other menu selections

Existing audio assets were reused where possible rather than creating an entirely new sound library.

#### Ball Hit and Ball Bounce Sounds

Two additional sound effects were imported:

**Ball Hit:** Intended to play when a valid player hit is applied to the ball.

**Ball Bounce:** Intended to play when the ball collides with the court surface.

The Ball prefab was configured with an Audio Source, and the BallController script was updated to support sound playback during ball interactions.

The implementation also includes a bounce cooldown to reduce repeated sound playback during rapid collisions.

Ball sound effects use 3D spatial audio so that sounds can originate from the ball's position within the virtual environment.

Photon Fusion RPC-based playback was added to support multiplayer audio events.

**Design Choice:** Background music establishes the atmosphere, while sound effects communicate interactions and gameplay events. Keeping these audio categories separate helps players hear important actions without the music becoming distracting.

**Testing Status:** Start Screen audio was tested in the Unity Editor and on Meta Quest. Full ball-hit and bounce audio behavior, including synchronization between multiple players, requires further gameplay verification.

**Screenshot:**

*Figure 5: Unity Audio Mixer showing the Master, Music, and SFX groups.*

[Insert Audio Mixer screenshot]

### 4. Improved Ball Physics and Targeting

Ball physics were refined to make hitting the ball easier and more predictable during VR gameplay.

Traditional physics-based ball movement can make it difficult for players to consistently direct the ball toward a desired location, particularly when using VR hand or controller movements.

To address this, a more controlled targeting approach was developed in the BallTest scene.

#### Court Subdivision

Each of the four court squares is divided into nine smaller target areas arranged in a 3×3 grid.

These subdivisions provide potential landing targets for the ball.

#### Controlled Ball Landing

When a player hits the ball with a reasonable velocity and angle:

1. The system evaluates the hit.
2. A target sub-square is selected.
3. The ball's trajectory is adjusted toward that target.
4. Physics continues to influence the ball's movement during flight.

This reduces dependence on completely uncontrolled physics while preserving the appearance of natural ball movement.

#### Out-of-Bounds Behavior

When a player's hit falls significantly outside the acceptable velocity or angle range, the ball follows its physics-based trajectory.

If the ball travels outside the playable court, it is marked as out of bounds.

#### Visual Debugging

Temporary visual markers were introduced to make the targeting system easier to test and understand.

These markers help identify the target areas and observe the ball's intended landing behavior.

They are intended to be removed after the behavior has been transferred and validated in the City scene.

**Design Choice:** The targeting system balances physical interaction with accessible gameplay. Players still perform a natural hitting motion, but reasonable hits are guided toward playable locations to reduce frustration.

**Current Status:** The targeting system has been developed in BallTest. Integration and validation in the City gameplay scene remain future work.

**Screenshot:**

*Figure 6: BallTest scene showing the 3×3 targeting subdivisions and temporary visual markers.*

[Insert BallTest screenshot]

---

## Intentional Design

The design of FourSquare VR focuses on creating a consistent, approachable, and engaging multiplayer VR experience.

The following sections describe the design decisions behind the Start Screen, environment, interactions, controller representation, audio, and gameplay mechanics.

### 1. Start Screen and First Impressions

The Start Screen is the first interface players encounter when launching FourSquare VR.

It presents the game's title and primary navigation options:

- Create Room
- Join Room
- How to Play
- Quit

The Start Screen uses a simple menu layout with customized fonts and a stylized skybox background.

The intention is to immediately communicate that FourSquare VR is a playful multiplayer game and allow players to understand how to begin without navigating through complicated menus.

The Create Room and Join Room options emphasize multiplayer participation as a central part of the experience.

After creating or joining a room, players enter a multiplayer lobby where they can view the room code and connected-player information.

The intended next step is to transition into the City gameplay environment once the required players have joined.

**Design Decision:** The Start Screen prioritizes clarity, recognizable actions, and a consistent visual style.

### 2. Visual Environment Design

The City environment uses stylized buildings, trees, and other meshes to create a colorful outdoor setting.

Custom materials and time-dependent skyboxes add visual variety while maintaining a consistent appearance.

The outdoor environment reflects the playground origins of Four Square and provides a setting for the multiplayer game.

**Design Decision:** A stylized environment was chosen to support a playful atmosphere and keep attention focused on the court and ball interactions.

### 3. VR Interaction Design

FourSquare VR combines menu-based interactions with physical gameplay interactions.

#### Menu Interaction

The VR interface uses XR Interaction Toolkit functionality to support controller-based menu navigation.

Controller rays allow players to select interface elements from a distance.

This approach is suitable for menu panels because players do not need to physically reach every button.

The XR Spatial Keyboard supports room-code entry while remaining inside the VR environment.

#### Gameplay Interaction

During gameplay, players interact with the ball using VR hand or controller movements.

The ball system evaluates movement speed and direction to determine whether a valid hit has occurred.

The improved targeting system is intended to make these interactions more forgiving while preserving the physical action of hitting a ball.

**Design Decision:** Distance-based interaction is appropriate for menu navigation, while direct physical interaction is a better match for hitting the ball during gameplay.

### 4. Hands and Controller Representation

The game uses XR controller-based interaction to support navigation and gameplay.

The intended design is to keep the player's hand or controller representation understandable and consistent with the action being performed.

For menu navigation, players need a clear indication of where they are pointing and which interface element is selected.

During gameplay, hand or controller movement should communicate the physical action of striking the ball.

The final visual appearance of hands/controllers, including whether their representation changes between menus and gameplay, remains a design detail to confirm and document as the experience is refined.

### 5. Audio Design

Audio supports the game's visual atmosphere and interaction feedback.

Different background music tracks distinguish the Start Screen from the City gameplay environment.

Button sounds provide immediate confirmation of menu interactions.

Ball-hit and bounce sounds are intended to help players recognize successful interactions and court collisions.

The Music and SFX mixer groups allow these sounds to be balanced independently.

**Design Decision:** Background music should support the experience without overpowering gameplay feedback.

### 6. Gameplay Accessibility

The ball-targeting improvements were introduced to make the game more approachable for VR players.

Rather than requiring perfectly accurate physics-based hits, the system guides reasonable hits toward valid court targets.

This supports a balance between natural movement and enjoyable gameplay.

**Design Decision:** The game should reward intentional player movement without requiring excessive precision from VR controllers.

---

## Multiplayer Networking and Room Management

Sprint 2 established the foundation for multiplayer room creation, joining, and session management using Photon Fusion.

The system includes:

- Four-letter room-code generation.
- Create Room and Join Room interfaces.
- Room-code validation and entry.
- A multiplayer lobby supporting up to four players.
- Connected-player count updates.
- Leave Room functionality.
- XR Spatial Keyboard integration.

Multiplayer room functionality was previously tested using Meta Quest as Player 1 and the Unity Editor on Mac as Player 2.

The City gameplay environment also contains Photon Fusion functionality for player assignments, ball interactions, and session management.

Continued integration and testing are needed to ensure that the entire flow works consistently from room creation through gameplay and the End Screen.

---

## Next Steps

Future development will focus on completing the full multiplayer gameplay loop and improving the overall VR experience.

### Gameplay and Physics

- Integrate the improved ball-targeting system into City.
- Remove temporary debugging markers after testing.
- Refine ball-hit detection, targeting, and out-of-bounds behavior.
- Validate gameplay rules and player elimination.

### Multiplayer

- Complete the Start Screen-to-City multiplayer transition.
- Verify synchronized ball movement and player interactions.
- Test four-player room capacity and gameplay.
- Improve handling of disconnections and room lifecycle events.

### Audio

- Test ball-hit and bounce sounds during actual multiplayer gameplay.
- Confirm audio playback across Quest and desktop clients.
- Balance background music and gameplay sound effects.
- Improve music transitions between scenes.

### User Experience

- Refine the appearance of VR hands and controllers.
- Improve menu interaction feedback.
- Test the game with users to identify confusing interactions.
- Improve the End Screen and complete the gameplay flow.

---

## Sprint 3 Progress Summary

During Sprint 3, FourSquare VR progressed beyond the networking-focused foundation established in Sprint 2.

The team introduced dynamic skyboxes, expanded the City environment with additional meshes and materials, improved the Start Screen's visual styling, and implemented background music and sound effects.

A more accessible ball-targeting approach was also developed to make VR ball interactions easier and more predictable.

These improvements support the project's overall goal of creating an immersive, visually cohesive, and enjoyable multiplayer Four Square experience.

The next development phase will focus on integrating these features into a complete gameplay loop and validating them through multiplayer testing.

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


