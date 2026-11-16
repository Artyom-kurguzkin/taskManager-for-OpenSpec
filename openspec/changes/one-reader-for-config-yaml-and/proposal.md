# Proposal

## Why

config.yaml is read by 8 path builders and 7 YAML deserializers across modules and the host, each failing differently. References like #wi12, #n3 and [[doc]] are parsed by about 10 regexes with 3 different boundary rules (modules, MCP, NotePage, Worklog), so the same note renders and links differently by screen. By the 3am rule both belong in OpenSpecStore, which owns the board's file format.

## What Changes

- OpenSpecStore becomes the single reader and writer of every x-rtm section of config.yaml, defaults included; other modules and the host call it.
- OpenSpecStore owns the reference grammar (parse and format #wiN, #nN, [[doc]]); notes, git mentions, MCP and the UI use it.
