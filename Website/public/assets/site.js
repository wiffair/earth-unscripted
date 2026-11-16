document.documentElement.classList.add("js");
const toggle = document.querySelector("[data-menu-toggle]");
const menu = document.querySelector("#navigation");
if (toggle && menu) {
  toggle.addEventListener("click", () => {
    const open = toggle.getAttribute("aria-expanded") !== "true";
    toggle.setAttribute("aria-expanded", String(open));
    menu.classList.toggle("is-open", open);
  });
  menu.addEventListener("click", (event) => {
    if (event.target.closest("a")) {
      toggle.setAttribute("aria-expanded", "false");
      menu.classList.remove("is-open");
    }
  });
  document.addEventListener("keydown", (event) => {
    if (
      event.key === "Escape" &&
      toggle.getAttribute("aria-expanded") === "true"
    ) {
      toggle.setAttribute("aria-expanded", "false");
      menu.classList.remove("is-open");
      toggle.focus();
    }
  });
}
// Native radio controls preserve keyboard, touch and screen-reader behaviour.
for (const radio of document.querySelectorAll('[name="graphics"]')) {
  radio.addEventListener("change", () => {
    const hd = radio.value === "hd" && radio.checked;
    document.querySelector("[data-graphics-copy]").textContent = hd
      ? "Richer lighting and reflections for more capable hardware. You can switch back at launch."
      : "Start here for better performance. The same roads, cars and progress, with lighter graphics.";
  });
}
