# Must never happen

Draft, waiting for the owner's approval. Each item names the tests that cover it, or says that no test covers it yet.

| # | The app must never | Covered by |
|---|---|---|
| 1 | leave the settings file half-written or replace good settings with a broken file. | `ConfigTests::save_cannot_write_original_settings_file_unchanged`, `ConfigTests::settings_saved_and_reloaded_with_no_temporary_file_left` |
| 2 | crash or refuse to start because the settings file is damaged. | `ConfigTests::damaged_settings_file_loads_defaults_without_crashing` |
| 3 | store login tokens on disk in plain text. | `ConfigTests::login_tokens_saved_encrypted_never_in_plain_text`, `ConfigTests::old_plain_text_tokens_migrated_and_removed_from_file` |
| 4 | leave the deepl:// link handler pointing at this app after a login, even if the app crashed during the login. | `SchemeHandlerTests::login_without_existing_handler_leaves_no_registry_keys_behind`, `SchemeHandlerTests::second_login_after_crash_still_restores_real_original_handler` |
| 5 | overwrite or delete the official DeepL app's link handler or other registry keys it did not create. | `SchemeHandlerTests::login_with_existing_handler_restores_original_command`, `SchemeHandlerTests::existing_scheme_key_without_command_gets_only_created_keys_removed`, `SchemeHandlerTests::existing_scheme_key_without_url_protocol_gets_added_value_removed`, `SchemeHandlerTests::start_without_unfinished_login_changes_nothing` |
| 6 | hang forever on a dead connection after a network switch or a dropped network. | `NetworkTests::network_change_mid_request_moves_request_to_fresh_connection`, `NetworkTests::request_hangs_past_attempt_limit_then_retried_and_succeeds`, `NetworkTests::every_attempt_fails_reports_offline_instead_of_hanging` |
| 7 | freeze or go blank when the network changes. The window must stay usable and say that it is offline. | No automated test. The owner checked it by hand in the running app on 2026-10-29, and translation kept working after a network switch. |
| 8 | throw away a valid login only because DeepL could not be reached. | `NetworkTests::token_refresh_while_offline_reports_offline_so_login_is_kept` |
| 9 | show the translation of older text after newer text was typed. | No test yet. |
| 10 | read browser cookies, saved passwords or other programs' data. | No test. The code that did this was removed in 1.1.0. |
| 11 | send the user's text anywhere other than DeepL. | No test yet. |
| 12 | swallow key presses or break Ctrl+C in other programs through the Ctrl+C+C keyboard hook. | No test yet. |
| 13 | leave the "Start with Windows" entry behind after it was switched off. | `StartupTests::start_with_windows_switched_off_removes_entry_and_keeps_other_programs`, `StartupTests::start_with_windows_switched_off_when_never_on_changes_nothing`, `StartupTests::start_with_windows_switched_off_also_removes_entry_under_old_app_name` |
| 14 | accept a login link that does not belong to the login it started, for example one sent by another program. | `LoginCallbackTests::login_callback_with_wrong_state_ignored`, `LoginCallbackTests::login_callback_without_running_login_ignored`, `LoginCallbackTests::malformed_login_callback_ignored_without_crash` |
| 15 | leave its files, its "Start with Windows" entry or a login's deepl:// handler behind after it was uninstalled, or delete the user's settings without asking. | No automated test. The uninstaller runs the app with `--uninstall-cleanup`, which uses the code covered by items 4, 5 and 13. |
| 16 | lose the settings or the login of an older version when it is updated, overwrite settings that already exist under the new name, or keep a "Start with Windows" entry that points at the old program file. | `ConfigTests::settings_of_old_app_name_moved_with_login_intact`, `ConfigTests::settings_of_old_app_name_never_replace_existing_new_settings`, `StartupTests::old_start_with_windows_entry_replaced_by_one_for_renamed_app`, `StartupTests::start_without_old_entry_does_not_switch_on_start_with_windows` |
