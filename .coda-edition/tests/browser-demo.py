"""Exercise the browser preview without touching native game data."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import os
import threading
from playwright.sync_api import sync_playwright

root = Path(__file__).resolve().parent.parent
artifacts = root / "artifacts"
artifacts.mkdir(exist_ok=True)
server = ThreadingHTTPServer(("127.0.0.1", 0), partial(SimpleHTTPRequestHandler, directory=str(root / "web")))
threading.Thread(target=server.serve_forever, daemon=True).start()
try:
    with sync_playwright() as playwright:
        executable = os.environ.get("CODA_TEST_CHROMIUM")
        browser = playwright.chromium.launch(executable_path=executable, headless=True)
        page = browser.new_page(viewport={"width": 1440, "height": 1000})
        errors = []
        page.on("pageerror", lambda error: errors.append(str(error)))
        page.goto(f"http://127.0.0.1:{server.server_port}/index.html")
        page.locator("#demo-badge").wait_for(state="visible")
        assert page.locator("#hero-heading").inner_text() == "All packed.\nReady to play."
        page.screenshot(path=str(artifacts / "home.png"), full_page=True)
        page.locator("#coda-boop").click()
        assert "Clipboard?" in page.locator("#coda-speech").inner_text()
        page.get_by_role("button", name="My Game", exact=True).click()
        assert page.locator("#mods").is_visible()
        page.locator('#mods [data-shelf="packs"]').click()
        assert page.locator("#packs").is_visible()
        page.locator('#packs [data-shelf="resourcepacks"]').click()
        assert page.locator("#resourcepacks").is_visible()
        page.get_by_role("button", name="Settings", exact=True).click()
        page.locator("#quiet-coda").check()
        assert "quiet-coda" in page.locator("body").get_attribute("class")
        page.locator("#save").click()
        assert "Demo settings applied" in page.locator("#settings-save-result").inner_text()
        page.get_by_role("button", name="Help", exact=True).click()
        assert page.locator("#logs").is_visible()
        page.locator("#help-home").click()
        page.locator("#play").click()
        assert "Browser demo only" in page.locator("#launch-message").inner_text()
        assert not page.locator("#play").is_disabled()
        page.locator("#profile-link").click()
        assert page.locator("#profile").is_visible()
        page.set_viewport_size({"width": 390, "height": 844})
        page.get_by_role("button", name="Home", exact=True).click()
        assert page.evaluate("document.documentElement.scrollWidth <= innerWidth")
        page.screenshot(path=str(artifacts / "mobile.png"), full_page=True)
        assert not errors, errors
        browser.close()
        print("PASS: shelves, navigation, Coda antics, quiet mode, settings feedback, browser launch boundary, profile and mobile layout")
finally:
    server.shutdown()
    server.server_close()
