# Round-4 backlog — UI/UX fluidity + landing buildout

Grilled 2026-08-03 (19 decisions; verbatim answers in `aidlc-docs/audit.md`; vocabulary in `CONTEXT.md`).
Loop process per issue: branch off pulled master → implement → rig-verify → PR → Copilot gate until a clean round → resolve threads → squash merge. Tick here in the same PR.

## Waves

| Wave | Issues | Why this order |
|---|---|---|
| A — visual system & quick wins | #123 #124 #125 #126 #141 | No schema except socials; the icon/button language (#123) is the vocabulary every later issue writes in; #126 flips week math before the schedule wave touches the rail; #141 lands the new platform brand assets early so every later screenshot carries them |
| B — inline edit, image UX, events | #127 #128 #129 #130 | #127 establishes inline-edit.js before #128 consumes it; #129 establishes the overlay before #130 (and wave D images) consume it |
| C — schedule island + markdown foundation | #131 #132 #133 | #131 makes the island date-aware before #132 pages weeks under a drag (riskiest — may slip); #133 lands MarkdownBlock before wave D's content depends on it |
| D — landing CMS, contact, disciplines | #134 #135 #136 #137 #138 #139 #140 | #134 creates section ordering + About that #135–#137 slot into; #138 is self-contained; #140 seeds last, consuming #135 programs + #139 ladders |

## Loop status

- [x] #123 — Icon-only buttons: green save / red delete + app-wide hover affordance (Glyph.razor SVG set; .btn--icon/--save/--del + --glow hover on all buttons; 31 call sites converted per semantic rule)
- [x] #124 — Finance chips: semantic colors (chip--ok/--bad + split CURRENT|BEHIND pill; zeros stay neutral)
- [x] #125 — Landing socials: TikTok + X, brand-color icons, run-together fix (AddSocialPlatforms; five hand-drawn brand marks in a flex-gapped badge row)
- [ ] #126 — Sunday-first weeks everywhere
- [ ] #141 — Platform brand: belt-patch logo + favicon adoption
- [ ] #127 — Inline edit pattern (pencil → save/cancel) + family rename
- [ ] #128 — Person page: name in header; contact merges into edit form
- [ ] #129 — Image edit overlay: click image, hover pencil; hide file inputs
- [ ] #130 — Event images: 1:1 flyer end-to-end
- [ ] #131 — Schedule drag: no modal after drag; cross-day drag; modal date field
- [ ] #132 — Schedule drag: edge-hover week paging
- [ ] #133 — Markdown everywhere (sanitized): Markdig + MarkdownBlock
- [ ] #134 — Landing: About section + admin-orderable sections
- [ ] #135 — Programs: entity, admin CRUD, landing section + modal
- [ ] #136 — Success stories: entity, admin CRUD, landing section
- [ ] #137 — Instructors: public portraits + rich landing modal
- [ ] #138 — Public contact form + spam wall + /admin/messages
- [ ] #139 — Custom rank ladders: admin UI for RankSystems + Ranks
- [ ] #140 — Multi-discipline demo seed: BJJ + Bootcamp + Muay Thai

## Per-issue notes

### #123 — Icon button system
Inline SVG set (pencil/floppy/trash/X/envelope), `.btn--icon` ghost square in `--ok`/`--bad`, aria-label + title mandatory, hover glow on ALL `.btn` variants (none exists today). Semantic rule: floppy = persist-a-record submits; trash = hard deletes only (income/expense DELETE); VOID/ARCHIVE/REMOVE/CANCEL SESSION/RECORD PAYMENT/SET PLAN/PUBLISH/MAKE PRIMARY-WARD keep text.

### #124 — Finance chips
`chip--ok`/`chip--bad` + two-tone split chip, existing tokens only. COLLECTED green (drops `chip--on`), OUTSTANDING red, CURRENT|BEHIND split, OTHER INCOME neutral at $0 / green positive.

### #125 — Socials
AddSocialPlatforms migration (SocialTikTok, SocialX varchar200) + AdminLanding fields + five brand-color inline SVGs in VISIT (flex-gapped — deletes the whitespace-stripped `<text> </text>` separator, the INSTAGRAMFACEBOOK bug).

### #126 — Sunday weeks
Shared `WeekOf()` (Domain) replaces `MondayOf()`: rail headers/?start= snap, member snap, `GetWeekAsync` materialization window, public strip. StatWeeks doc-comment updated. Materialization regression tests across the Sat/Sun boundary.

### #141 — Platform brand adoption
Joshua-authored belt-patch mark + favicon (Claude Design → synced to design/assets/, spec design/foundations/logo.html). wwwroot/favicon.svg + head link; `BrandMark.razor` (field = var(--tenant), bar/stripes fixed — never restriped, never tenant-colored); auth-shell + footer lockups (mono lockup in chrome/footers); tenant-recolored patch replaces accent-block crest fallbacks; min 20px in UI.

### #127 — Inline edit + family rename
`inline-edit.js` (media-crop.js mold): text+pencil ⇄ input + save/cancel icon buttons (the #123 SVG set), Enter/Esc; cancel = client revert, save = normal POST (PRG); no-JS = plain form. AdminFamilyDetail h1 (Rename section deleted; posts /admin/family-actions/rename) + member MyFamily rename row.

### #128 — Person page reshape
h1 pencil → [First][Last][save][cancel] inline; edit-person form drops names, keeps DOB + checkboxes, absorbs Phone + SMS consent (SetContactAsync semantics: clearing number clears consent); Contact section deleted.

### #129 — Image overlay
MediaCropUpload overlay mode: preview is the click target (label-for), hover dim ~55% + stroke pencil fade; file-input row hidden when JS active; dashed placeholder when no image. Applied: portrait, logo, hero.

### #130 — Event images
AddEventImage (`GymEvent.ImagePath`), 1:1/1024 crop below Title in publish form, disk at gyms/{gym}/events/, authed same-gym serving endpoint (NOT anonymous /media), feed card image above chip+header (card grows only when present), detail image between cap and details card.

### #131 — Drag correctness
Cards stop being naked `?edit=` anchors racing server-synced preventDefault; island navigates deliberately on clean-click pointerup only; pointer capture. Cross-day drag (column from ClientX, 2D preview). `UpdateSessionAsync` gains date param (fan-out tested). SSR modal gains DATE input.

### #132 — Edge-hover paging
~600ms hover on edge zones pages the week under a live drag (island swaps week data, keeps grab), repeatable, URL syncs after drop. Riskiest interactive piece; may slip a wave.

### #133 — Markdown
Markdig: raw HTML disabled, EmphasisExtras subset (`++u++`, `~~strike~~`), soft-break=hard-break. ONE audited MarkdownBlock (the only MarkupString in the repo). Applied: Event.Details, StaffProfile.Bio, TrainingEntry.Notes. Caps hint under markdown textareas. Contact bodies excluded by design. Injection tests.

### #134 — About + section order
AddLandingSections on GymSettings: AboutTitle/AboutText (markdown), SectionOrder (validated keys about,programs,schedule,instructors,stories,visit; default funnel, About first), ProgramsTitle/Intro, StoriesTitle/ImagePath. PublicGym renders per order (hero first, foot last, Contact rides Visit, empty auto-hide, anchors follow). /admin/landing: per-section cards + ▲▼ POST forms.

### #135 — Programs
Program entity (Title, Description md, ImagePath, SortOrder, Archived) + AddPrograms. NOT a ClassType (CONTEXT.md distinction). Admin CRUD, 1:1 image via #129 overlay, landing cards → modal (image/title/description), public /media allow-list extended to gyms/{gym}/programs/*.

### #136 — Success stories
SuccessStory entity (Body md, AttributedTo, SortOrder, Archived) + AddSuccessStories. Section-level image (StoriesImagePath), public allow-list gyms/{gym}/stories.*, cards render body + attribution.

### #137 — Instructor public presence
Doctrine change + ADR 0003: portraits staff-only EXCEPT unarchived Instructor-role persons → anonymous endpoint scoped to exactly that set (role loss/archive re-privatizes by construction); conditional upload hint. StaffProfile.Hobbies + AddStaffHobbies. Card hover glow; modal: portrait, name, BeltBar if ranked, ExperienceSummary, gym About text, Bio (markdown), Hobbies, close control. Auth-matrix tests (instructor 200 / member 404 / archived 404).

### #138 — Contact form + messages
ContactMessage + AddContactMessages. Public CONTACT block with Visit: first/last/email/phone/message; email-or-phone required; phone mask "(###) ###-####" as-you-type (static JS), digits normalized server-side. Spam: honeypot + signed min-time + per-IP rate limit + link-cap/keyword/length heuristics + strict format + best-effort MX (fail-open — lab DNS unreliable). /admin/messages (read/unread, pager) + nav MESSAGES + unread badge + envelope glyph; in-app admin fan-out; optional GymSettings.ContactForwardEmail via existing SMTP path. Bodies plain + pre-wrap, never markdown.

### #139 — Rank ladders UI
Admin create/edit/archive RankSystems + Ranks (name, band/bar colors, order, stripes); RankService write ops; award flow/BeltBar/roster filter already generic. Glossary already promised custom systems.

### #140 — Multi-discipline seed
DemoSeeder: Bootcamp + Muay Thai class types/templates, Muay Thai prajioud ladder, three Programs, sample About + stories. Random(7) fiction determinism preserved ($510 intact); no binary images. Lab demos need a fresh slug.

## Verification standard
Suite green + new tests per issue (WeekOf/materialization windows; date-move fan-out; markdown injection inert; portrait auth matrix; contact spam wall: honeypot/min-time/rate-limit/email-or-phone/MX fail-open; program/story CRUD + ordering; ladder CRUD end-to-end; seeder integrity). Rig per issue: fresh `gymstation-rig-db` postgres + idempotent script + seed-demo + curl antiforgery login — NEVER touch `gymstation-db` (live lab). Playwright breakpoint screenshots for the landing (sections, modals, contact) and the icon sweep. Releases + lab deploys per wave; migrations (A #125; B #130; D #134 #135 #136 #137 #138) each grep-verified in the staged migrate script before the image bump.
