# T05: 10-configuration-persistence-hardening 05

**Slice:** S11 — **Milestone:** M001

## Description

Surface the typed `IConfigurationService.Failures` stream as a concise banner in the existing `SettingsViewModel`, and clear the banner on the next successful save (D-27). The banner explicitly states that settings were restored to last saved values when an optimistic save rolled back (D-30). This is the public-UX completion of the typed-failure path established by Plans 02 and 03; the cleaning workflow path is closed by Plan 04.

Purpose: D-24/D-25/D-27/D-29/D-30/D-31 land here at the public UI surface. Wave 3 work parallel with Plan 04.

Output: `SettingsViewModel.cs` gains a `[ObservableProperty] PersistenceBannerText`, `[NotifyPropertyChangedFor(nameof(HasPersistenceBanner))]`, a `Failures` subscription guarded for design-time construction, a `PersistenceResults` clear-on-success subscription, `SaveAsync` flush-before-close behavior, and a Dispose hook for subscriptions. `SettingsWindow.axaml` gains a minimal visible banner bound to `PersistenceBannerText` / `HasPersistenceBanner`. Tests cover the banner state machine and XAML binding presence.

## Must-Haves

- [ ] "SettingsViewModel subscribes to IConfigurationService.Failures via CallbackObserver<T> marshaled through IUiDispatcher and exposes a typed banner status (D-24, AGENTS.md threading rule)."
- [ ] "On save failure, the ViewModel maps ConfigPersistenceFailure to a concise banner string AND explicitly states that settings were restored to last saved values (D-30)."
- [ ] "On the next successful save/reload/flush, the ViewModel banner clears (D-27)."
- [ ] "Banner content is text-only — no modal dialog (D-29)."
- [ ] "No new retry UI is added (D-31)."
- [ ] "Banner stays empty for hash-echo-only watcher events (D-25 — silent rejections do not surface to UI; coordinator filters those out before publishing)."
- [ ] "SettingsWindow.axaml binds a visible text banner to PersistenceBannerText/HasPersistenceBanner so the user can actually see the recoverable failure path (review HIGH concern)."

## Files

- `AutoQAC/ViewModels/SettingsViewModel.cs`
- `AutoQAC/Views/SettingsWindow.axaml`
- `AutoQAC.Tests/ViewModels/SettingsViewModelTests.cs`
