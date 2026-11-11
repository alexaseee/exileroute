# ExileRoute

[Русская инструкция](README.md)

**[Download the Windows installer](https://github.com/alexaseee/exileroute/releases/latest/download/ExileRoute-Setup.exe)**

ExileRoute displays your route, tasks, and passive skill tree over Path of Exile 1. Use the **editor** to choose a profile and prepare your campaign plan. The **overlay** displays that plan in game and identifies your current act from the game log.

## 1. Install and launch ExileRoute

1. Make sure you have Windows 10 or 11 x64 and .NET Framework 4.8 or later.
2. Download and run the installer. Choose an installation folder. On the final screen, select **Create a desktop shortcut** if you want one.
3. In the Path of Exile 1 graphics settings, enable borderless windowed mode so the overlay appears over the game.
4. Open **ExileRoute** from the Start menu or the desktop shortcut. The editor and overlay will open. Use **RU** or **EN** in the top-right corner of the editor to change the language of both windows, the location names in the catalog, and the built-in routes. Text in custom profiles appears as you saved it.

## 2. Connect the game log

1. Open **Settings** in the editor to check the log status and file path. On first launch, ExileRoute looks for the log in Steam libraries and the default folders for the standalone client.
2. If there is no file path, click **Find log** to search again or **Browse…** to select `logs\Client.txt` in the game folder. If you have several Path of Exile 1 installations, select the log for the one you are playing.
3. Enter a campaign area with your character or move to another area. The status should show the act number and area name.

If you see **Waiting to enter a game area**, enter an area with your character. If you see **Game log is unavailable**, check the path and select the file again if you moved the game. In a hideout, the Labyrinth, or a map, the status reads **Outside the campaign**. The list for the current act reappears when you return to a campaign area.

## 3. Follow a built-in route

1. In the **Profile** list, choose **Default** or **Exile Leveling**. Both include offline routes for Acts 1–10. **Exile Leveling** follows a league-start route that visits the Library and helps Alira.
2. If the overlay is showing the task list, click **Route** in its header. This is where the built-in profiles display their campaign steps. Click **Tasks** to switch to the task list. The editor tabs and overlay modes can be switched independently.
3. To preview an act, select its number in the editor, then click **Preview**. The overlay shows the chosen act in the current **Route** or **Tasks** mode. While previewing, click another act number to see that act. Click **Back to auto** to resume tracking the act from the log. If the passive tree is open, switch back to the route or tasks first.
4. Use the title bar's minimize button to hide the editor if needed. The area name in the overlay header updates as you move through the campaign. The route keeps its scroll position when you move between areas within an act. Use the mouse wheel to scroll to the card you need.
5. Check or uncheck items in the first column of the editor or on the cards in the overlay. Checkmarks sync between both windows. The **Completed** counter shows progress for the current list. Tasks and route notes have separate counters, and each profile saves its own progress.

To reset an act's progress, choose a profile and act number on the **Tasks** or **Route** tab. Click **Reset checkmarks** above the act numbers and confirm. The button is active when the selected act has checkmarks. It clears the marks in both the task list and the route for that act.

## 4. Position, resize, and control the overlay

- Drag the overlay by its header to move it. Resize either window by dragging an edge or corner. ExileRoute remembers the size of both windows and the position of the overlay.
- Open **Settings** in the editor and set the overlay transparency between **0%** and **80%**. At **0%**, the window is opaque. The setting takes effect immediately, saves automatically, and applies to all profiles. Click **Done** to close the settings.
- Click **Lock** on the overlay or press the shortcut. The default is `Ctrl+Shift+9` using **9** on the top row of the keyboard. In click-through mode, clicks go to the game, and the mouse wheel still scrolls long lists when the pointer is over them. Press the shortcut again to interact with the overlay.
- To change the shortcut, open **Settings**, choose a key and modifiers under **Click-through shortcut**, and click **Apply**. The shortcut takes effect immediately, is saved for all profiles, and appears on the overlay. If it is unavailable, choose another combination. **Restore default** switches back to `Ctrl+Shift+9` when that combination is available.
- Click **Edit** to return to the editor. You can also restore it from the taskbar. Closing the editor exits ExileRoute and closes the overlay.

## 5. Create and manage profiles

Each profile has its own tasks, route notes, and checkmarks for all ten acts, along with a list of builds. To edit a built-in route, duplicate its profile. You can also save checkmarks and builds in either built-in profile.

| Profile button | What it does |
| --- | --- |
| **New** | Enter a unique name and click **Create**. The editor opens a blank profile. |
| **Duplicate** | Select the source profile, click the button, and confirm the name of the copy. The copy includes the tasks, route notes in their current order, checkmarks, build list, and selected build. You can then edit each profile independently. |
| **Save** | Writes your current data to disk immediately. Changes also save automatically. The status at the bottom of the editor shows the result. |
| **Delete** | Select a custom profile, click the button, and check the name in the dialog. After two seconds, the confirmation button becomes available. Click **Delete** in the dialog. |

To switch campaign plans, choose another profile from the list. The editor and overlay immediately show its contents. On the next launch, ExileRoute opens the last selected profile.

### Add and edit tasks

1. Choose a custom profile and act number, then open the **Tasks** tab. Click **+ Add task**.
2. In the optional **Condition** field, enter a reminder for when to do the task, such as “Reach level 12.” In the required **Action** field, enter what to do, such as “Pick up the Poisonous Concoction skill gem.” Check the task off manually when you finish it.
3. To insert a gem name, place the cursor where it should go or select the text you want to replace, then click **Insert gem** next to that field. Search for part of the Russian or English name. Use the **Gem type** list to select **All gems**, **Skill gems**, or **Support gems**.
4. Select the cell with the name in the language you want and click **Insert**. You can also double-click the name. The name goes into the field as plain text.
5. Click **Save** in the task dialog. To edit a task, select its row and click **Edit** or double-click its text. To remove a task, select the row, click **Delete**, and confirm.

### Add and edit route notes

1. Choose a custom profile and act number, then open the **Route** tab. Use **Act locations** to select an area for the new note ahead of time if needed.
2. Click **+ Add note**. In the dialog, choose a location, enter a note, or do both. For a general note about the act, choose **No location** and enter text. You can add several cards for the same location.
3. Click **Save**. The new note appears at the end of the route. Select it and use the **↑** and **↓** buttons to move it into position.
4. To change a note or its location, select the row and click **Edit**. You can also double-click the location or note cell. To remove the card from the route, click **Delete** and confirm.

## 6. View a passive skill tree

1. Choose a profile, open the **Passives** tab, and click **+ Add**. Paste a Path of Exile 1 build link in the form `https://pobb.in/…`, give it a name if you like, and click **Save**. The build appears in the profile list and opens in the overlay. Repeat these steps to add another build. A link saved in the previous version appears in the list automatically.
2. Choose a build from the **Build** dropdown in the overlay. ExileRoute remembers this choice for the profile. Once the build loads, choose a **Preset** to highlight its nodes and connections. Hover over a node to see its name and effects in English.
3. Zoom with the mouse wheel over the tree or with the **−** and **+** buttons. At higher zoom levels, drag the tree with the left mouse button. Click **Fit** to show the whole tree. If click-through mode is on, use the shortcut shown at the bottom of the overlay to turn it off.
4. The tree icon in the overlay header switches between the passive tree and the previous campaign list. Select a build in the editor and click **Load build** to open it or retry a download. Click **Edit** to change its name or link. To remove it from the profile, click **Delete** and confirm.

The first load of a build and tree version requires an internet connection. Locally cached data is available offline. If loading fails, check the link and your connection, then retry from the editor. If tree data is missing for the selected preset, choose a preset that uses a different tree version if the build includes one.

## 7. Save expressions for in-game search

1. Open the **Regex** tab in the editor and click **+ Add**.
2. Paste the search expression and click **Save**. All profiles and acts share the same list of expressions.
3. Click **Copy** on the expression's row to copy the full text. When you see **Copied**, switch to the game and paste the expression into the search field with `Ctrl+V`.
4. To update an expression, click **Edit** on its row. To remove it, click **Delete** and confirm.

## 8. Save, update, and uninstall

ExileRoute stores its data locally in `%LOCALAPPDATA%\PoEActOverlay\settings.json`. This file holds your profiles and progress, shared Regex list, language, manually selected game log path, editor tab, overlay mode, and window settings. If the editor reports **Could not save**, check that the folder is writable, then click **Save** to try again.

To update, close ExileRoute and run the new installer. It will suggest your previous installation folder. To uninstall, close ExileRoute and select it in the Windows list of installed apps. Your local data is kept after updating or uninstalling.

## Source code

`build.cmd` builds the app. `.\build-installer.ps1` builds the installer using NSIS 3.

To edit the original **Default** profile, close ExileRoute, run `build.cmd author`, and open `ExileRoute-Author.exe` from the project folder. Use **RU** and **EN** to select the version of the route to edit. Changes are written to `BuiltInProfile.json`, which supplies both versions to the standard build.

[MIT license](LICENSE) · [Game data sources](THIRD_PARTY_NOTICES.md)

ExileRoute is an independent project unaffiliated with Grinding Gear Games.
