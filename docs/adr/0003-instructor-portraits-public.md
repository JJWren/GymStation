# 3. Instructor portraits are public

Date: 2026-08-03

## Status

Accepted

## Context

From v1 the platform's stance was that member portraits are staff-only:
uploaded by staff, served solely through a GymStaff-gated endpoint, with the
anonymous `/media` route regex-locked to logo/hero. The upload UI promised
"STAFF-ONLY, NEVER PUBLIC", and a code review suggesting public portraits was
explicitly rebutted with that doctrine.

Round 4 (#137) put instructor cards on the public landing page with photos.
That collides with the doctrine — and with the promise made when existing
portraits were uploaded.

## Decision

Instructor portraits are public. Joshua's ruling, verbatim rationale:
instructors are essentially already public figures for the gym; their photos
are only attached by staff and are for internal systems and for visitors to
the gym page.

Mechanics:

- A dedicated anonymous endpoint serves a portrait ONLY for an unarchived
  Person holding the Instructor role (`InstructorPortraits.PubliclyVisible`).
  Role loss or archiving re-privatizes the photo by construction — no flag to
  forget.
- Everyone else's portrait stays staff-only through the existing gated route;
  the anonymous `/media` catch-all still never serves portraits.
- The upload hint is conditional: instructors see "shown on the public page",
  everyone else keeps the never-public wording.

## Consequences

- Publishing is retroactive for existing instructor portraits: the photos
  staff already uploaded become publicly reachable when this ships. That is
  the intended reading of "public figures for the gym" — gyms manage consent
  with their coaches offline, as they do for any marketing material.
- Once served publicly, a photo may be cached or archived by third parties;
  removing the role stops serving but cannot recall copies.
- Members' and children's portraits remain staff-only. Any future widening
  (e.g. member opt-in) needs its own decision — this ADR covers instructors
  only.
