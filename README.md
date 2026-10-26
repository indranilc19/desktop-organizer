# Desktop Organizer

A full-screen home screen and launcher for Windows. It knows everything on your PC and your external drives, keeps your media and reading library tidy, and answers questions like *"do I have this book?"*.

It's a single C# program that Windows builds on first run with the compiler it already ships. You don't need Visual Studio, an SDK or an installer.

## What it does

**Find anything, fast**
- Opens with **Alt+Space** (or **Ctrl+Alt+Space** if that is taken), from the tray, or at sign-in. Type to search apps, files, folders, books, films and photos.
- Ask a question such as *"do I have any Agatha Christie books?"* or *"one piece vol 21"*. You get **Yes**, **No** or **Not sure**, the best matches, and links (Goodreads, MyAnimeList, Comic Vine, Google, Claude).
- Has categories: Movies, TV Shows, Anime, Animation, Manga, Comics, Books, Photos, Trips, Magazines and more.

**A catalogue of every drive, even unplugged ones**
- Every file on every drive is recorded under the drive's serial number. You can still find files on an external drive that isn't connected, and it tells you which drive to plug in.
- Records video quality (resolution, codec, HDR, bitrate) from MKV/MP4/MOV/AVI headers.
- Writes an Excel workbook and a browsable HTML dashboard: drives, films and shows by language, director, cast, genre and decade, and home videos by trip.

**Films, shows and anime**
- Groups files into titles and looks them up on Wikidata (director, cast, language, genre, runtime, IMDb id).
- Uses real posters only. It takes the poster next to the files (tinyMediaManager, Kodi and Plex style), or downloads one from Wikipedia (portrait images only) or TVmaze. It never shows a random video frame.
- **Find duplicate films & episodes** (tray menu): keeps the best copy, judged by resolution, HDR, source (remux > Blu-ray > web) and codec-adjusted bitrate, and moves the others to `<drive>\_Duplicates (review)`. Different editions (extended, director's cut, dubbed) are never treated as duplicates.
- Scraper hints: when a film has no `.nfo` but was matched on Wikidata, it writes a minimal `.nfo` with the English title and IMDb id. tinyMediaManager, Kodi and Plex can then match romanised or foreign titles by id.

**Covers that are actual covers**
- ePub cover images, the first page of CBZ and CBR comics (CBR uses Windows' built-in `tar`), and the first page of PDF magazines and guides (rendered with Windows' own PDF engine).

**Sort new files**
- Watches Downloads, the Desktop and any folders you add. It works out what each new file is, renames it, and files it on the right drive:
  - **Films:** `Title (Year)`
  - **Episodes:** `Show [Year]\Show - 1x01 - Episode Title`, with the title looked up on TVmaze
  - **Photos and home videos:** `YYYY\YYYY-MM Event\YYYY-MM-DD - Event - 001`, using EXIF or video creation dates and matching them to trips
  - **ePubs:** `Publisher - EYear - Title`
  - **Manga:** volumes and chapters
  - **Magazines and documents:** filed into their own folders
- Big media goes to your *media drive* and reading to your *library drive*. By default these are the largest and second-largest external drives; you can set them in `settings.json`.

**Keeps working when you move things**
- **Refresh locations** re-scans all connected drives.
- If you open something that has moved, it finds the same file by name and size wherever it went.

**Tools** (`tools\`)
- `Keep drive letters.bat`: pins external drives to fixed letters by volume label, optionally at every sign-in.
- `Check USB ports.bat`: reports controllers, problem devices and the link speed of connected drives.

## Install

1. Download or clone this repository into any folder, for example `Documents\Desktop Organizer`.
2. Double-click **`Launch.vbs`**. The first run copies `settings.example.json` to `settings.json` and `sources.example.json` to `sources.json`, builds `DesktopOrganizer.exe` (a few seconds), and starts it.
3. Edit `settings.json` (your name, top-bar buttons, background photo folders, media and library drives) and `sources.json` (the folders to index; copy entries from `_examples` for your external drives). Then press **F5**.
4. Optional: tray icon → **Open at sign-in**.

The program rebuilds itself whenever the source changes, so updating is just pulling the repo and running `Launch.vbs` again.

## Requirements

- Windows 10 or 11 with .NET Framework 4.x (built in).
- Internet access is optional. It's used for Wikidata, Wikipedia and TVmaze lookups, all keyless and rate-limited.

## Recommended companion: tinyMediaManager

To scrape and rename your existing library, set the TV show renamer in tinyMediaManager to:

| Field | Pattern |
|---|---|
| TV show folder | `${showTitle} [${showYear}]` |
| Season folder | *(empty)* |
| Episode file | `${showTitle} - ${seasonNr}x${episodeNr2} - ${title}` |

If a title isn't found, search with its English name or paste its IMDb id (`tt…`) into the search box. The `.nfo` hints this tool writes do the second part automatically.

## Data and privacy

Everything stays on your PC:
- The index and logs are in `data\`.
- The drive catalogue, posters and covers are in `Documents\Drive Catalog`.

The only network calls are public lookups against Wikidata, Wikipedia and TVmaze.

## Layout

```
Launch.vbs            first-run setup, build, start
build.ps1             compiles src\*.cs with the Windows C# compiler
src\Core.cs           search engine, thumbnails / covers, settings, app entry
src\Home.cs           the full-screen window, shelves, browse tiles, tray
src\Catalog.cs        per-drive catalogue, video headers, Excel export
src\Media.cs          titles, Wikidata, posters, duplicates, home videos, dashboard data
src\Inbox.cs          "Sort new files"
dashboard\            HTML template for the drive catalogue page
art\                  browse-tile artwork
tools\                drive letters, USB check, PDF cover renderer
```

## License

MIT. See [LICENSE](LICENSE).
