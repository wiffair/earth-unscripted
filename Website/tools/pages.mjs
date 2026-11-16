const github = "https://github.com/wiffair/earth-unscripted";
export const escape = (value) =>
  String(value).replace(
    /[&<>"']/g,
    (char) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        char
      ],
  );
const intro = (label, title, copy) =>
  `<header class="page-intro wrap"><p class="eyebrow dark">${label}</p><h1>${title}</h1><p class="lead">${copy}</p></header>`;
const carCopy = {
  "vw-splittie": {
    tag: "Peace & Seafoam",
    description:
      "A split-window camper made for taking your time. Seafoam and warm ivory paint, chrome trim, daisies and a roof rack with a longboard give it a well-travelled feel.",
    features: [
      "Rear-wheel drive and a relaxed road-car setup.",
      "Recorded air-cooled VW engine audio in the current beta.",
      "A driver-ready cabin, dashboard, steering wheel and open window apertures.",
      "Fixed Peace & Seafoam livery, with curtains, luggage and a longboard.",
    ],
    notes:
      "An original procedural model inspired by a classic VW camper. Vehicle names and marks do not imply endorsement.",
    licence:
      "Original pack content: MIT, with an additional CC BY-SA 4.0 option for the reviewed original artwork. Engine recordings retain their separate CC BY / CC0 notices.",
  },
  hekla: {
    tag: "Built for the climb",
    description:
      "An Icelandic Formula Offroad inspired machine, with a reinforced open frame, a V8 and enormous paddle tyres. Take a different line through the landscape.",
    features: [
      "Four-wheel drive with physical paddle tyres.",
      "Long-travel suspension and a reinforced frame.",
      "Forgiving landing behaviour for off-road jumps.",
      "V8 engine sound and paint options in the current beta.",
    ],
    notes:
      "A fictional vehicle inspired by the Icelandic Formula Offroad series. The specifications shown here are game tuning, rather than a real competition vehicle’s specification.",
    licence:
      "Original pack content: MIT. Keep the licence and any separately attributed recording notices supplied with the pack.",
  },
};
export function carCard(car) {
  return `<article class="car-card"><a href="/cars/${car.id}" class="car-photo"><img src="/assets/cars/${car.id}-front.png" alt="${escape(car.title)} pack model, front three-quarter studio view" width="1200" height="800" loading="lazy"></a><div class="car-card-copy"><p class="eyebrow dark">Vehicle pack / v${car.version}</p><h2><a href="/cars/${car.id}">${escape(car.title)}</a></h2><p>${escape(carCopy[car.id].description)}</p><div class="card-bottom"><span>Free · ${car.credits} in-game credits</span><a class="text-link" href="/cars/${car.id}">View car &amp; download ↗</a></div></div></article>`;
}
function carPage(car) {
  const copy = carCopy[car.id];
  return `<div class="wrap breadcrumbs"><a href="/cars">Car packs</a><span>/</span>${escape(car.title)}</div><section class="wrap car-detail"><div class="car-gallery"><img data-gallery-main src="/assets/cars/${car.id}-front.png" alt="${escape(car.title)}, front three-quarter studio view" width="1200" height="800" fetchpriority="high"><div class="gallery-thumbs">${["front", "rear", "side"].map((view) => `<a href="/assets/cars/${car.id}-${view}.png" data-gallery-src="/assets/cars/${car.id}-${view}.png" data-gallery-alt="${escape(car.title)}, ${view} studio view" aria-label="Show ${view} view of ${escape(car.title)}"${view === "front" ? ' aria-current="true"' : ""}><img src="/assets/cars/${car.id}-${view}.png" width="1200" height="800" alt="${view} view" loading="lazy"></a>`).join("")}</div><p class="image-caption">Studio renders of the released pack model. Lighting differs in-game.</p></div><div class="car-detail-copy"><p class="eyebrow dark">${escape(copy.tag)} / v${car.version}</p><h1>${escape(car.title)}</h1><p class="lead">${escape(copy.description)}</p><p class="car-price">Free <span>${car.credits} in-game credits</span></p><a class="button dark-button pack-download" href="${car.url}">Download vehicle pack <span aria-hidden="true">↓</span></a><p class="download-small">.eucar · ${Math.round(car.bytes / 1000)} KB · Invited GitHub account required</p><dl class="car-facts"><div><dt>Drive</dt><dd>${car.physics.frontDriveFraction === 0 ? "Rear-wheel" : "Four-wheel"}</dd></div><div><dt>Tyre diameter</dt><dd>${(car.physics.wheelRadius * 2).toFixed(2)} m</dd></div><div><dt>Game mass</dt><dd>${car.physics.massKg.toLocaleString("en-GB")} kg</dd></div><div><dt>Version</dt><dd>${car.version}</dd></div></dl></div></section><section class="wrap section car-more"><div><p class="eyebrow dark">In the pack</p><h2>${car.id === "hekla" ? "Take the rough way." : "Slow down. Look around."}</h2><ul class="feature-list">${copy.features.map((text) => `<li>${escape(text)}</li>`).join("")}</ul><p>${escape(copy.notes)}</p></div><div class="install-panel"><h3>Add it to your garage</h3><ol><li>Download the .eucar file above.</li><li>In the beta, open <strong>Cars &amp; Garage</strong> and the vehicle-pack importer.</li><li>Import the file, then select the car. This pack costs 0 credits.</li></ol><p>Use the current <a href="/download">Earth Unscripted beta</a>. Existing player downloads may need the camper imported separately.</p><details><summary>Licence and download integrity</summary><p>${escape(copy.licence)}</p><a href="/assets/cars/${car.id}-LICENSE.txt">Read the pack licence</a><p>SHA-256</p><code>${car.sha256}</code></details></div></section>`;
}
const stationDescriptions = {
  "iron-and-ash":
    "Guitars, rough edges and songs that stay with you. IRON & ASH ranges from melodic alternative rock and grunge to garage rock and punk, hosted by Alex Mercer.",
  rewind:
    "Bright synths, guitar pop, disco grooves and slower late-night songs. REWIND brings original music with an 80s and 90s feel, hosted by Jamie Bell.",
};
export function stationCard(station) {
  return `<article class="station-card ${station.id}"><a class="station-dial" href="/radio/${station.id}"><span>FM ${station.frequency_mhz.toFixed(1)}</span><strong>${escape(station.name)}</strong><em>${escape(station.tagline)}</em><span class="dial-lines" aria-hidden="true">▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏</span></a><div class="station-card-copy"><h2>${escape(station.name)}</h2><p>${escape(stationDescriptions[station.id])}</p><a class="text-link" href="/radio/${station.id}">Station, music &amp; downloads ↗</a></div></article>`;
}
export function radioPlayer(stations, defaultStation) {
  const first = stations.find((s) => s.id === defaultStation);
  const song = first.tracks[0];
  return `<section class="radio-section wrap" aria-label="Radio music previews" data-radio-player data-default-station="${defaultStation}"><div class="radio-cabinet"><div class="radio-brand">EARTH UNSCRIPTED <span>FM STEREO</span></div><div class="radio-display" aria-live="polite"><div class="radio-station-line"><strong data-radio-station>${escape(first.name)}</strong><span data-radio-frequency>${first.frequency_mhz.toFixed(1)} MHz</span></div><h2 data-radio-title>${escape(song.title)}</h2><p data-radio-artist>${escape(song.artist)}</p><span data-radio-status>Press play · 45-second music previews</span></div><div class="radio-tuner" aria-hidden="true">88 <span>▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏▏</span> 108 FM</div><div class="radio-interactive"><div class="radio-transport"><button type="button" data-radio-prev aria-label="Previous music preview">◀</button><button type="button" data-radio-play aria-label="Play music preview" aria-pressed="false">▶ PLAY</button><button type="button" data-radio-next aria-label="Next music preview">▶</button></div><label class="radio-volume">Volume <input data-radio-volume type="range" min="0" max="1" step="0.05" value="0.6"></label></div><div class="radio-progress"><progress data-radio-progress value="0" max="45" aria-label="Preview progress"></progress><span data-radio-time>0:00 / 0:45</span></div><div class="radio-presets" aria-label="Radio presets">${stations.map((s) => `<button type="button" data-radio-preset="${s.id}" data-station-name="${escape(s.name)}" data-station-frequency="${s.frequency_mhz.toFixed(1)}" aria-pressed="${s.id === defaultStation}">${s.frequency_mhz.toFixed(1)} <span>${escape(s.name)}</span></button>`).join("")}</div><audio class="radio-audio" data-radio-audio controls preload="none" src="${song.preview}" aria-label="Music preview player"></audio></div><div class="radio-playlist"><div class="playlist-heading"><h3>On the dial</h3><span>45-second previews</span></div>${stations.map((s) => `<div data-radio-playlist="${s.id}"${s.id === defaultStation ? "" : " hidden"}>${s.tracks.map((t, i) => `<a class="track-row" href="${t.preview}" data-track-id="${t.id}" data-track-station="${s.id}" data-track-title="${escape(t.title)}" data-track-artist="${escape(t.artist)}"><span class="track-number">${String(i + 1).padStart(2, "0")}</span><span><strong>${escape(t.title)}</strong><small>${escape(t.artist)} · ${escape(t.genre)}</small></span><span aria-label="Play preview">▶</span></a>`).join("")}</div>`).join("")}<p class="preview-credit">Original music by Richard Cole, with fictional artist and station identities. <a href="/credits">CC BY-SA 4.0 · credits</a>. Excerpts begin 30 seconds into each recording; fades and peak limiting applied.</p></div></section>`;
}
const radioDownload = `<a class="button dark-button" href="${github}/releases/download/v0.24.0-beta.1/EarthUnscripted-TwoStations-3.0.0.euradio">Download both stations <span aria-hidden="true">↓</span></a><p class="download-small">Radio pack v3.0.0 · .euradio · Invited GitHub account required</p>`;
export function additionalPages(cars, radio) {
  const stations = radio.stations;
  return [
    {
      path: "/cars",
      title: "Car packs",
      description:
        "Meet the cars: images, details and versioned vehicle pack downloads for Earth Unscripted.",
      body:
        intro(
          "The garage",
          "Find your next car.",
          "Something for the long way round. Something for the road less travelled. Explore the models, then add a pack to your garage.",
        ) +
        `<section class="wrap car-grid">${cars.map(carCard).join("")}</section><aside class="wrap pack-help"><h3>How vehicle packs work</h3><p>Import a .eucar file through Cars &amp; Garage in the beta. Packs can have an in-game credit price; both of these are currently free. <a href="/download">Get the current beta ↗</a></p></aside>`,
    },
    ...cars.map((car) => ({
      path: "/cars/" + car.id,
      title: car.title,
      description: carCopy[car.id].description,
      body: carPage(car),
    })),
    {
      path: "/radio",
      title: "Radio stations & music",
      description:
        "Tune into IRON & ASH and REWIND. Hear original music and explore the radio packs in Earth Unscripted.",
      body:
        intro(
          "The radio",
          "Good company.<br><em>Wherever you’re going.</em>",
          "Two fictional stations, twelve original songs and music for the journey. Pick a preset and listen before you drive.",
        ) +
        radioPlayer(stations, "iron-and-ash") +
        `<section class="wrap station-grid">${stations.map(stationCard).join("")}</section><section class="wrap radio-download section"><h2>Bring them along.</h2><p>The current beta includes both stations. The radio pack is also available separately, with music, station intros and jingles.</p>${radioDownload}<a class="text-link" href="${github}/releases/download/v0.24.0-beta.1/EarthUnscripted-Radio-Music-1.0.0.zip">Download the MP3 collection ↗</a></section>`,
    },
    ...stations.map((station) => ({
      path: "/radio/" + station.id,
      title: station.name + " " + station.frequency_mhz.toFixed(1),
      description: stationDescriptions[station.id],
      body:
        `<div class="wrap breadcrumbs"><a href="/radio">Radio stations</a><span>/</span>${escape(station.name)}</div>` +
        intro(
          station.frequency_mhz.toFixed(1) +
            " FM / " +
            escape(station.presenter),
          escape(station.name) + "<br><em>" + escape(station.tagline) + "</em>",
          escape(stationDescriptions[station.id]),
        ) +
        radioPlayer(stations, station.id) +
        `<section class="wrap station-info section"><div><p class="eyebrow dark">The station</p><h2>Six songs.<br>A sound of its own.</h2><p>${station.id === "rewind" ? "A decades-spanning sound: synth-pop, guitar pop, disco-pop, soft rock and Spanish-language pop. The songs are original recordings with fictional artist identities." : "Alternative rock, melodic punk, acoustic grunge and heavier guitar tracks. The songs are original recordings with fictional artist identities."}</p><p>In the game, ${escape(station.presenter)} introduces the music, with station jingles between tracks. The web radio plays music excerpts.</p></div><div class="install-panel"><h3>Get ${escape(station.name)}</h3><p>Both stations are included in the current game download. For a separate import, the two-station pack includes all twelve songs, intros and jingles.</p>${radioDownload}<p>Import the .euradio file through the beta’s radio-pack menu, then tune to ${station.frequency_mhz.toFixed(1)} FM.</p></div></section>`,
    })),
    {
      path: "/packs",
      title: "Packs",
      description:
        "Cars and radio stations for Earth Unscripted. Browse versioned packs and previews.",
      body:
        intro(
          "Make it yours",
          "A little more<br><em>for the journey.</em>",
          "Explore cars, hear the radio stations and download the packs you want to take along.",
        ) +
        `<section class="wrap hub-grid"><a href="/cars"><span class="eyebrow dark">The garage</span><h2>Car packs ↗</h2><p>Images, descriptions and vehicle downloads.</p><img src="/assets/cars/vw-splittie-front.png" width="1200" height="800" alt="VW Splittie car pack" loading="lazy"></a><a href="/radio"><span class="eyebrow dark">On the dial</span><h2>Radio packs ↗</h2><p>Meet the stations and preview their music.</p><div class="hub-radio" aria-hidden="true">101.7 <span>IRON &amp; ASH</span><br>96.4 <span>REWIND</span></div></a></section>`,
    },
    {
      path: "/credits",
      title: "Credits & licences",
      description:
        "Artwork, car model and radio music credits for Earth Unscripted.",
      body:
        intro(
          "Made for the journey",
          "Credits &amp; licences.",
          "The people, music and artwork behind this website.",
        ) +
        `<section class="wrap document credits-copy"><h2>Original code and artwork</h2><p>Earth Unscripted by Richard Cole. Original website code and technical documentation use MIT. The brand cover, icon, route illustration and favicon use CC BY-SA 4.0 where copyright applies, with AI assistance recorded in the project’s provenance. The cover is artwork rather than a gameplay screenshot.</p><h2>Car pack images</h2><p>Studio renders use the actual released car-pack meshes and material colours. The Hekla model retains its MIT licence. The reviewed VW Splittie model and livery retain MIT with an additional CC BY-SA 4.0 option for original artwork. See the <a href="/assets/cars/hekla-LICENSE.txt">Hekla notice</a> and <a href="/assets/cars/vw-splittie-LICENSE.txt">VW pack notice</a>. VW marks and vehicle names do not imply endorsement.</p><h2>Radio music</h2><p>All twelve preview recordings are supplied original music by Richard Cole, under <a href="https://creativecommons.org/licenses/by-sa/4.0/">CC BY-SA 4.0</a>. Artist, station and presenter names are fictional creative identities. Each preview is a 45-second excerpt starting 30 seconds into the original, with short fades and peak limiting. Song and artist credits appear beside each playable track and in its MP3 tags.</p><p><a href="/assets/radio-preview-provenance.json">Recording source hashes and adaptation details</a></p><h2>The game and its data</h2><p>The game’s geographic data and third-party assets keep their own terms. Read the project’s <a href="${github}/blob/main/LICENSE_SCOPE.md">licence scope</a> and <a href="${github}/blob/main/Provenance/README.md">full credits</a> on GitHub; private testing currently requires collaborator access.</p></section>`,
    },
  ];
}
