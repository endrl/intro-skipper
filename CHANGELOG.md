# Changelog

## Unreleased

* Task Timer is no longer configured by deafult. We listen for MediaLibrary changes instead. (Change back in options)

## v0.4.0.0 (2023-11-02)

* Remove creatorId (sync with server implementation)

## v0.3.0.0 (2023-09-25)

* Add options to control listener
* Round to two decimal places
* Improve log messages

## v0.2.0.0 (2023-05-28)

* Blacklisting with db
* Enable movies credits detection

## v0.1.0.0 (2023-05-04)

* Outdated, removed from repository!
* Initial release
* New features
  * Detect ending credits in television episodes
  * Add support for using chapter names to locate introductions and ending credits
  * Add support for using black frames to locate ending credits
* Internal changes
  * Move Chromaprint analysis code out of the episode analysis task
  * Add support for multiple analysis techinques
* Breaking Change
  * Removed all server and frontend influencing mods
  * Removed EDL handling
