# Gear Guide

A Dalamud plugin for FFXIV that shows the best gear for your current class and level in a native window built like the gear side of the Character window.

## Features

- **Looks like the Character window.** The class header, the equipment slots down both sides and the item level star use the game's own layout and textures (taken from `Character.uld`). Empty slots show the game's slot silhouettes.
- **Native tooltips and menus.** Hovering a slot shows the game's own item tooltip. Clicking a slot opens the game's item menu: the inventory menu (Equip, Link, Search for Item...) for pieces you own, and the item-link menu (Try On, Link, Search for Item...) for anything else. Entries other plugins add to those menus show up too.
- **Source filters.** Round toggles under the middle panel pick where recommendations may come from:
  - **Owned**: your inventory, armoury chest, what you're wearing, and your saddlebag once you've opened it this session.
  - **Vendors**: gear sold for gil by NPCs.
  - **Crafted**: gear with a recipe (assumed high quality).
  - **Market Board**: gear other players can sell you.
  - **Other Sources**: gear you can't buy or craft, such as dungeon drops, tomestone and seal exchanges and quest rewards.
  - **Live Listings**: when on, Market Board only counts gear that's listed on your data center right now, checked with [Universalis](https://universalis.app). When off, anything sellable counts.
- **Recommended Gear button.** Like the Character window's, it puts on the best set you can make from the gear you own, one piece at a time, even where a slot shows a better piece you'd still have to get (a high-quality copy, say). Slots where you own an upgrade you aren't wearing get a badge.
- **Stat comparison.** Where the Character window shows your character, Gear Guide lists the recommended set's key stats next to what you're wearing now, with the difference.
- By default, the **Recommended Gear** button in the game's Character window opens Gear Guide instead of the game's Recommended Gear window. Turn **Recommended Gear button opens Gear Guide** off in the settings to get the game's window back.

Recommendations follow your job, level, race, gender and Grand Company. For combat jobs pieces are rated mainly on your main stat and weapon damage, then secondary stats; crafters on Craftsmanship, Control and CP; gatherers on Gathering, Perception and GP. A piece that covers other slots (a two-handed weapon, a robe) is only recommended if it beats the separate pieces it replaces.

## Commands

- `/gearguide` - open or close the window
- `/gearguide settings` - open settings

## Installation Instructions

1. Open the game chat and type `/xlsettings`, then click the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL into the empty box at the bottom:
   ```
   https://raw.githubusercontent.com/mcbro12345/DalamudPlugins/main/pluginmaster.json
   ```
3. Click the **+** button to add it, then **Save and Close**.
4. Type `/xlplugins` to open the Plugin Installer, search for "Gear Guide," and click **Install**.

That's it. Updates will show up in the Plugin Installer automatically.

## Privacy

With **Market Board** and **Live Listings** both on, Gear Guide sends the item IDs it's considering and your data center's name to `universalis.app` to look up listings. Nothing else about you or your character is sent. Turn Live Listings off to stop these requests.

## Credits

Gear Guide wouldn't exist without these projects:

- [KamiToolKit](https://github.com/MidoriKami/KamiToolKit) by MidoriKami - MIT
- [Universalis](https://universalis.app) - live market board listings

Full details in `THIRD_PARTY_NOTICES.md`.

## Contributing

Issues and PRs are welcome - see `CONTRIBUTING.md`.

## AI-assisted development

Parts of this codebase were built with AI coding tools. See `AI-GENERATED-NOTICE.md` for details.

## Disclaimer

Gear Guide is an unofficial, fan-made project. It's not affiliated with or endorsed by Square Enix or the Dalamud project. FINAL FANTASY XIV and related trademarks belong to their respective owners.

## License

MIT - see `LICENSE`. Third-party components keep their own licenses (see `THIRD_PARTY_NOTICES.md`).
