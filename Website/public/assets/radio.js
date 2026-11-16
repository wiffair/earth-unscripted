const player = document.querySelector("[data-radio-player]");
if (player) {
  const audio = player.querySelector("[data-radio-audio]");
  const playButton = player.querySelector("[data-radio-play]");
  const status = player.querySelector("[data-radio-status]");
  const allTracks = [...player.querySelectorAll("[data-track-id]")];
  const presets = [...player.querySelectorAll("[data-radio-preset]")];
  let station = player.dataset.defaultStation;
  let tracks = allTracks.filter(
    (track) => track.dataset.trackStation === station,
  );
  let index = 0;
  audio.volume = 0.6;
  const time = (seconds) =>
    `${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, "0")}`;
  function refresh() {
    const playing = !audio.paused;
    playButton.textContent = playing ? "Ⅱ PAUSE" : "▶ PLAY";
    playButton.setAttribute(
      "aria-label",
      playing ? "Pause music preview" : "Play music preview",
    );
    playButton.setAttribute("aria-pressed", String(playing));
    status.textContent = playing
      ? "Playing · 45-second preview"
      : "Paused · 45-second preview";
  }
  async function play() {
    try {
      await audio.play();
    } catch (error) {
      if (error.name !== "AbortError") {
        status.textContent =
          "Playback could not start. Press play to try again.";
        audio.controls = true;
        audio.classList.add("show-native");
      }
    }
  }
  function selectTrack(next, autoplay) {
    index = (next + tracks.length) % tracks.length;
    const track = tracks[index];
    audio.pause();
    audio.src = track.href;
    player.querySelector("[data-radio-title]").textContent =
      track.dataset.trackTitle;
    player.querySelector("[data-radio-artist]").textContent =
      track.dataset.trackArtist;
    for (const row of allTracks) {
      row.classList.toggle("is-playing", row === track);
      if (row === track) row.setAttribute("aria-current", "true");
      else row.removeAttribute("aria-current");
    }
    player.querySelector("[data-radio-progress]").value = 0;
    player.querySelector("[data-radio-time]").textContent = "0:00 / 0:45";
    refresh();
    if (autoplay) void play();
  }
  function selectStation(id, autoplay) {
    station = id;
    tracks = allTracks.filter(
      (track) => track.dataset.trackStation === station,
    );
    const preset = presets.find((button) => button.dataset.radioPreset === id);
    player.querySelector("[data-radio-station]").textContent =
      preset.dataset.stationName;
    player.querySelector("[data-radio-frequency]").textContent =
      preset.dataset.stationFrequency + " MHz";
    for (const button of presets)
      button.setAttribute("aria-pressed", String(button === preset));
    for (const list of player.querySelectorAll("[data-radio-playlist]"))
      list.hidden = list.dataset.radioPlaylist !== id;
    selectTrack(0, autoplay);
  }
  playButton.addEventListener("click", () => {
    if (audio.paused) void play();
    else audio.pause();
  });
  player
    .querySelector("[data-radio-prev]")
    .addEventListener("click", () => selectTrack(index - 1, true));
  player
    .querySelector("[data-radio-next]")
    .addEventListener("click", () => selectTrack(index + 1, true));
  player
    .querySelector("[data-radio-volume]")
    .addEventListener("input", (event) => {
      audio.volume = Number(event.target.value);
    });
  for (const preset of presets)
    preset.addEventListener("click", () =>
      selectStation(preset.dataset.radioPreset, !audio.paused),
    );
  for (const track of allTracks)
    track.addEventListener("click", (event) => {
      event.preventDefault();
      if (track.dataset.trackStation !== station)
        selectStation(track.dataset.trackStation, false);
      selectTrack(tracks.indexOf(track), true);
    });
  audio.addEventListener("play", refresh);
  audio.addEventListener("pause", refresh);
  audio.addEventListener("ended", () => selectTrack(index + 1, true));
  audio.addEventListener("timeupdate", () => {
    player.querySelector("[data-radio-progress]").value = audio.currentTime;
    player.querySelector("[data-radio-time]").textContent =
      `${time(audio.currentTime)} / 0:45`;
  });
  audio.addEventListener("error", () => {
    status.textContent =
      "This preview could not load. Choose another track or try again.";
  });
  selectTrack(0, false);
  player.classList.add("radio-ready");
}
