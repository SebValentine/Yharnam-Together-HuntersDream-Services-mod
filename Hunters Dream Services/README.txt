HUNTER'S DREAM SERVICES - a mod for the Yharnam Together launcher (0.1)
========================================================================
Lets Yharnam Together use The Hunter's Dream (thehuntersdream.com) for the game's own online services while the
co-op itself stays on your host's server (Radmin VPN, Tailscale, ...):

  comes from The Hunter's Dream      messages, bloodstains, wandering ghosts (death replays), lantern counts,
                                     chalice search and sharing (glyphs), standard chalice rituals
  stays on your host's server        the team lobby, the relay, everything that makes the co-op work

Free software, GPL-2.0-or-later. It changes no Yharnam Together file.

INSTALL

  1. Put this folder ("Hunters Dream Services") into <Yharnam Together>\mods\ (create "mods" if it is not there).
  2. Close the launcher and the game. Double-click "Install.cmd". It builds bin\ythd-probe.exe from shim\YtHdProbe.cs
     (with the C# compiler that comes with Windows) and sets BB_PROBE in the launcher's Advanced > Extra variables.
  3. Open the launcher: "Mods" now lists "Hunters Dream Services". Press PLAY as usual.

ON / OFF

  - The switch in the launcher's Mods page. It is read at every start, nothing else to do.
  - Or double-click "Turn ON.cmd" / "Turn OFF.cmd" (they edit the same switch; close the launcher first).
  - "Status.cmd" shows the switch and whether The Hunter's Dream answers right now.
  OFF = exactly the way Yharnam Together worked before: the host's own server answers the game's services.


SETTINGS 

  (ythd.ini in this folder, read at every start)
  community=   the community server (default https://thehuntersdream.com)
  playlogs=    1 = send the game's play logs to the community server's API as the Windows port does
  fallback=    1 = check before each start that the community server answers, else keep the host's services

GOOD TO KNOW

  - Everyone in the party should use the same setting. Mixed parties were not tested.
  - ON means your game talks to The Hunter's Dream (your online name and what you write or leave behind).
  - After a Yharnam Together update the launcher setting should stay (Yharnam Together says settings are kept). If the
    mod stops working or bin\ythd-probe.exe is gone, run Install.cmd again.

UNINSTALL
  Close the launcher and the game, double-click "Uninstall.cmd" (removes bin\ythd-probe.exe and the launcher setting),
  then delete this folder if you like.

HOW IT WORKS
  The launcher writes user\host_overrides.custom.json so the game's server address (ss4.scej-network.jp:20443) is the
  host's backend. This mod's program sits between run.py and the game engine (BB_PROBE), writes its own
  user\host_overrides.hunters-dream.json with the community server as the game server (the summon addresses stay on the
  host's shadNet WebAPI, as in Yharnam Together's own run.py default) and starts the engine with that file.
