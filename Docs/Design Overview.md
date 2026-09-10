# **Project: Naval MOBA v2**

## **Overview**

A multiplayer top-down 3D naval shooter set in the WW1/WW2 era. Guns are aimed manually and elevation ranges are set, aircraft are sent on sorties and torpedoes are fired, while manuevering to avoid enemy fire and other threats. Character progression in the form of ship trees, sailors evolve to improve skills and gain veterancy with continued performance without dying.

## **Tech Stack**

- Unity with URP (Universal Renderer, 3D)
- C# with assembly definitions per module
- Input System package (not legacy Input Manager)
- Networking library TBD (most likely Photon Fusion)
- Server-authoritative framework from the start. RPCs are sent by client to execute actions, visuals (if any) are created and run locally by client. Server validates RPC input via rewinding timeline and checking outcome. Lag compensation and client-side prediction are used to keep gameplay smooth and consistent.
- Network culling is used to limit cheating by not providing any client data to another client unless absolutely necessary.

## **Folder Structure**

```
Assets/
	Audio/ - SFX and music files for use in the game
	Code/ - Top-level folder for all scripting files
		Client/ - All Client-Side Logic Goes Here
			Audio/ - Logic for handling sound effects, music playback, etc.
			Ballistics/ - Logic for projectile physics, spawning, and resolution
			Controllers/ - Ship movement, camera control, turret control, aircraft control, etc.
			Loadout/ - Ship configuration and instantiation
			Sailors/ - Logic related to sailor creation, progression, death, and interactions
			Ship/ - Logic for player ship, including equipment mounting, armor interaction, and anything else related to a player's ship and its systems
			UI/ - Logic for controlling visual overlays, including UI elements, aiming aids such as tracer lines, and anything else used as a visual aid that is not present in the actual game world
			VFX/ - Logic for handling visual effects and similar
		Common/ - Logic that belongs to both client and server, or doesn't really belong in either one
			SODefs/ - Definitions for types of ScriptableObjects
		Editor/ - Anything used within Unity Editor to assist in the development process
			Helpers/ - Tools and aids used for authoring content
			Debug/ - Tools & aids used for tracking down errors and problems, including logging
		Server/ - All Server-Side Logic Goes Here
			Simulation/   - per-tick update loop
			Network/      - transport, interest management, snapshots
	Content/        - item, weapon, material data assets
		Ships/ - various subfolders for content created from a ScriptableObject def
		Turrets/ - "..."
		Ammo/ - "..."
	Models/ - Folder & subfolders for raw .FBX or .OBJ files before they are created into prefabs	
	Prefabs/ - Folder & subfolders for GameObjects that have been created into prefabs
	Scenes/
		Dev/          - test scenes for individual systems
		Main/         - real game scenes
	Settings/
	Plugins/          - Unity-reserved, native plugins
	ThirdParty/       - external imports, asset store packages

```

Do not put any files at the `Assets/` root. It only contains subfolders. Do not create folders before there is content for them. Do not createa extra sub-folder divisions without a clear need.

## **Module Dependency Rules**

Every module in `Code/` has its own `.asmdef`. Dependencies flow one direction only:

```
|-> Code
  |-> Client
    |-> Ships, UI, Audio
  |-> Common
	|-> SODefs, Ballistics
  |-> Server
    |-> Simulation, Network

```

Nothing lower should reference anything higher. If a low-level module needs something from a higher one, the shared type moves into a higher-level folder or gets abstracted behind an interface. The compiler enforces this through asmdef references, which is the point.

## **Architectural Decisions**

- **Sea and movement is continuous.** World is not tile or grid-based. Ships, aircraft, and projectiles all move in smooth, continuous world space. Cover, LOS, and destructibility are resolved against continuous geometry/positions, not a grid. (Corrected: a prior draft of this doc said the world was tile-based. That was wrong and has been removed.)
- **Server-authoritative from day one.** Even in single-player prototype, structure code so the simulation could run on a headless server. Clients send inputs, server simulates, clients predict and reconcile.
- **LOS runs on the server.** Per-player visibility drives network interest management. Clients only receive data for entities they can see. This is the primary anti-cheat mechanism.
- **Vision modified by world state and player states** Each player's visible area is drawn from their ship, spotter skill and function, and augmented by their aircraft's sight ranges and active weather patterns. Allies are given less-precise information via radio communications that happen between ships**
- **Lighting clarity is a layer over visibility, not a gate on it.** Visibility is binary, clarity is graded.
- **Sound is a gameplay system, primary perception via visualization.** Even if a ship cannot see a ship, the reports from cannons and bombs detonating can be heard and felt for much further distances, providing visual information through audio systems.
- **Separate data from simulation from rendering.** Systems are testable in isolation.

## **Audio System Design**

The audio system is the genre-defining perception system alongside vision. Spatialized stereo audio is supplementary; visual cues carry the spatial information. Goals and design notes:

- **Propagation truth versus perception.** Circumstances such as a damaged ship with a raging inferno on the deck, or a screen of escorts firing their cannons at full speed make it difficult to perceive and hone in on sounds from that same direction, such as enemy return fire. A perception filter sits between the propagator and the renderer, degrading the truth based on the listener's situation, sailor status effects (concussion, deafening, etc.), and other modifiers. In multiplayer, two listeners hearing the same event can see and hear different things, but the underlying physics is identical.
- **Sailor skill progression, training level, and fighting condition affects perception clarity as well as gameplay modifiers** Novice seamen are less accustomed to the signs of a ship on the horizon before its smoke stack gives it away completely. Damage control teams take longer to repair battle damage, engineers are less able to exploit every last ounce of performance from the engines, and gunnery teams aren't as rehearsed as elite veterans, resulting in slower reloads, and less accurate firing solutions. Experienced sailors perceive these cues faster and execute their trained roles faster, more accurately, and more safely.
- **Acoustics matter.** Sound carries further at night than day, especially over the ocean. Sound carries further in cold air than warm. During the day, the sun warms the Earth's surface, creating a layer of warm air near the ground/water and cooler air above. Because sound travels faster in warm air, the bottom of the sound wave moves faster than the top, causing the wave to refract (bend) upward into the sky where it dissipates. At night, the water or land cools down quickly, leaving a layer of cool air trapped beneath a layer of warmer air aloft—a phenomenon known as a temperature inversion. “This inversion refracts sounds downward...” when they try to escape into the sky. This creates a natural "whisper chamber" or acoustic tunnel over the flat ocean surface, trapping the sound waves close to the water where they can travel immense distances. While sound technically moves faster through warm air molecules, it carries further horizontally in cold air. Over the ocean, cold weather (especially clear, chilly mornings) regularly creates the stable temperature inversions mentioned above. The cold air sitting directly on top of the water forces sound waves to continuously bend back downward toward your ears rather than scattering upward into the atmosphere. And, Contrary to the popular myth that "fog magnifies sound," fog actually dampens sound waves, particularly higher frequencies. Fog is composed of millions of tiny, suspended liquid water droplets. When a sound wave hits these droplets, “water droplets cause scattering of the noise, where noise is both refracted and absorbed by the water particles, lowering its traveling distance.” The sound energy is essentially converted into microscopic amounts of heat and lost. Foggy days are usually completely still with very little wind or human activity. Because the "background noise floor" drops drastically, faint sounds seem much louder by comparison, even though the fog itself is fighting against the sound wave. This is why traditional maritime foghorns use an incredibly low pitch—low-frequency sounds have massive wavelengths that can easily bypass tiny water droplets, allowing them to pierce through the dampening fog. For subarmiunes, sound travels roughly five times faster and incredibly further underwater because water molecules are packed tightly together. Deep in the ocean, there is a boundary layer called the SOFAR channel (Sound Fixing and Ranging channel). In this channel, a precise mix of cold temperature and high pressure creates an underwater acoustic tunnel. “The channeling of sound waves allows sound to travel thousands of miles without the signal losing considerable energy”—which is exactly how whales communicate across entire ocean basins.

## **Current Status**

Prototype phase. Core gameplay systems being implemented: movement, vision, sound, ballistics, ship loadout (shipyard) configurator, progression systems and sailor development.  

Implemented:
- Ship movement & control
- Camera control
- Manual turret control system. Aiming guidelines
- Basic 1-person ship spawning, turret instantiation
- Single ship type for development testing
- Single turret type for development testing

In-Progress:
- Gunnery systems and ballistics simulation

Next steps:
- Finish gunnery/ballistics
- Sailor implementation
- Specific ship loadout configuration & persistent ship setups
- Shipyard
- Author more ship types and turret options.