# Tasks

## 1. pass-1

- [x] 1.1 Pooled connection; S3Storage disposable, cached per address <!-- agent: Claude -->
- [x] 1.2 Stat cache shared by scan, git guard, marker repair and status line; no bytes held <!-- agent: Claude -->

## 2. pass-2

- [x] 2.1 No second pass after the sync's own writes (engine reports written paths) <!-- agent: Claude -->
- [x] 2.2 Idle check: conditional manifest GET, 60 s focused / 5 min unfocused, jitter <!-- agent: Claude -->

## 3. pass-3

- [x] 3.1 Parallel transfer, streamed downloads, base reuse before GET <!-- agent: Claude -->
- [x] 3.2 Compact manifest JSON, history 20 to 3 <!-- agent: Claude -->
