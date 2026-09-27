# RedirectPrinter

VB.NET WinForms utility (`RedirectPrinters`) that remaps a user's Windows printer connections from `substitutes.xml`. Each mapping has old/new print server and queue (wildcard `*` moves every queue on a server). It deletes the old connection, adds the new one via winspool, can set the default printer, and logs to the event log. Built for Empired Limited.

**Source last updated:** 2008-03-31  
**Language:** VB.NET  
**Target:** .NET 2.0  
**Output:** WinForms exe

## Solution structure

| Project | Language | Type | Purpose |
|---------|----------|------|---------|
| `RedirectPrinter` | VB.NET | WinForms exe (.NET 2.0) | `frmMain` reads substitutes.xml and remaps printer connections |

## How to open

Open `RedirectPrinters.sln` in Visual Studio 2008 or later. Copy `RedirectPrinter/substitutes.xml.example` to `substitutes.xml` beside the exe and fill in server/queue names before running.

## Requirements

- Visual Studio 2008, .NET Framework 2.0

## Attribution and provenance

Working copy from my Historical Dev folder.

From my Historical Dev archive (folder `RedirectPrinter`). Assembly: RedirectPrinters, Empired Limited, Copyright 2008. Real printer-server names from the original mapping file were not imported; use the example XML.

## License

MIT License. Copyright (c) 2026 VaderConsulting.
