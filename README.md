# Unity MMD Tools : Demo Project

This is a demo application that showcases features of [**Unity MMD Tools (UMT)**](https://github.com/CandidumGames/UnityMMDTools).
Unity version : 6000.3.16f1

## Controls

- **Load PMX** : load an MMD character and import it into the scene. In the browser select the folder containing the model (so its textures load with it); on desktop pick the model file directly.
- **Physics: Live / Baked** : toggle whether physics is simulated live or baked into the motion.
- **Load Motion** : pick an MMD motion file and play it on the loaded character.
- **Load Camera** : pick an MMD camera motion file and use it to drive the camera.
- **Reset Anim** : return the character to its default pose.
- **Camera: User / VMD** : switch between freely orbiting the camera and following the loaded camera motion.
- **Reset View** : return the user camera to its starting position.
- **Hide UI (H)** : show or hide the on-screen interface (also toggled with the **H** key).
- **Rewind** : jump the motion back to the start.
- **Play / Pause** : start or pause playback.
- **Scrub bar and frame box** : drag the slider or type a frame number to jump to any point in the motion.

## Live demo

Try it in the browser: [Unity MMD Tools Demo](https://play.unity.com/en/games/7ab34080-389d-481e-af6e-1319e6ff874f/build)

## Third-party packages

This project and toolkit build on the following open-source projects:

- [lilToon](https://github.com/lilxyzw/lilToon) : toon shader used for materials
- [Netherlands3D FileBrowser](https://github.com/Netherlands3D/FileBrowser) : cross-platform file/folder picker

## License

MIT : see [LICENSE.md](LICENSE.md).
