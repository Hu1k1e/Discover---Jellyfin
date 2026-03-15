/* ================================
   CSS Pack
================================== */

/* ---------- Theme imports ---------- */
@import url('https://cdn.jsdelivr.net/gh/Hu1k1e/H-TV-CSS/final%20css%20import.css');

/* ---------- Core variables ---------- */
:root{
  --accent: 0, 166, 81;
  --rounding: 12px;
  --bg-color: rgb(24, 24, 24);
  --transparent-bg: #10101000;
}

/* ---------- Typography ---------- */
@font-face{
  font-family: 'AaltoSansPro';
  font-style: normal;
  font-weight: 400;
  src: url('/web/fonts/AaltoSansPro-Regular.ttf');
}
@font-face{
  font-family:'AaltoSansPro';
  font-style:normal;
  font-weight:400;
  src:url('/web/fonts/AaltoSansPro-Regular.woff2') format('woff2');
}
@font-face{
  font-family:'AaltoSansPro';
  font-weight:700;
  src:url('/web/fonts/AaltoSansPro-Bold.woff2') format('woff2');
}
@font-face{
  font-family:'AaltoSansPro';
  font-weight:300;
  src:url('/web/fonts/AaltoSansPro-Light.woff2') format('woff2');
}

html, body, button, input, select, textarea {
  font-family: "AaltoSansPro", sans-serif !important;
  font-size: inherit !important;
}

.emby-tab-button{ font-size: large; }
.navMenuOptionText{ font-size: medium !important; }

/* ---------- Backdrop + login ---------- */
.backdropImage { 
    filter: blur(12px) brightness(50%) !important; 
}

body:has(.itemDetailPage) .backdropImage,
body:has(.detailsPage) .backdropImage {
    filter: none !important; 
}

#loginPage{
  background: url(https://i.imgur.com/9vL4iNf.png) !important;
  background-size: cover !important;
}

/* ---------- Global Tweaks & Bounds ---------- */
/* Prevent sideways overflow */
html, body { 
    margin: 0 !important;
    padding: 0 !important;
    width: 100vw !important;
    max-width: 100vw !important; 
    overflow-x: hidden !important; 
}
.skinBody, .mainAnimatedPages {
    max-width: 100vw !important;
    overflow-x: hidden !important;
}
img, video, canvas{ max-width:100% !important; }

.section0 .sectionTitle{ display:none; }
.verticalSection-extrabottompadding{ margin-top: 2em; }

/* Hide Trailers tab in Movies */
.mainDrawer:has(.navMenuOption-selected[href^="#/movies.html"]) + .skinHeader .emby-tab-button[data-index="2"]{
  display:none !important;
}

/* Hide Jellyfin Enhanced entries */
#jellyfinEnhancedSettingsLink,
#jellyfinEnhancedUserPrefsLink,
a[data-itemid="Jellyfin.Plugin.JellyfinEnhanced.CalendarPage"],
a[data-itemid="Jellyfin.Plugin.JellyfinEnhanced.DownloadsPage"],
div.pluginMenuOptions > h3.sidebarHeader,
.jellyfinEnhancedSection > .sidebarHeader {
  display:none !important;
}

#jellyfin-enhanced-panel .tab-button.active{ color:#fff !important; }
#jellyfin-enhanced-panel span.modified-indicator{ color:#00c853 !important; }

/* ---------- Channels tab layout tweaks ---------- */
#channelsTab .card{ width:100%; }
#channelsTab .cardBox{ margin:.1em; }
#channelsTab .cardDefaultText{ display:none; }
#channelsTab .cardPadder-square{ padding-bottom:30px; }

#channelsTab .cardScalable button.cardContent{
  height:20px;
  background-size:auto 100%;
  width:70px;
  margin:5px !important;
}
#channelsTab .cardScalable button.defaultCardBackground1,
#channelsTab .cardScalable button.defaultCardBackground2,
#channelsTab .cardScalable button.defaultCardBackground3,
#channelsTab .cardScalable button.defaultCardBackground4,
#channelsTab .cardScalable button.defaultCardBackground5{
  background-color: transparent;
}
#channelsTab .cardFooter{ position: unset; padding:0; }
#channelsTab .cardFooter .cardText:nth-child(1){ position:absolute; left:80px; top:0; }
#channelsTab .cardFooter .cardText:nth-child(2){ position:absolute; right:140px; top:5px; width:250px; }
#channelsTab .cardFooter .cardText:nth-child(3){ position:absolute; right:390px; top:5px; width:100px; }
#channelsTab .cardOverlayFab-primary{
  width: inherit; height: inherit; margin-top: inherit; margin-left: inherit; font-size: 88%;
  top: -5px; right: 79px; position: absolute; left: unset; background-color: transparent; padding:.25em;
}
#channelsTab .cardOverlayFab-primary:hover{ background-color: rgba(0, 164, 220, 0.2); color: #00a4dc; transform: unset; }
#channelsTab .cardOverlayButton-br{ bottom:-5px; top: unset; }
@media screen and (max-width:65em){
  #channelsTab .cardFooter .cardText:nth-child(2),
  #channelsTab .cardFooter .cardText:nth-child(3){ display:none; }
}

/* ---------- Detail page button tweaks (desktop) ---------- */
.layout-desktop .mainDetailButtons{ margin-top:1.5em; }
.layout-desktop .btnPlay{ border-radius:2em; background-color:white; transition: background-color 0.2s ease, box-shadow 0.2s ease !important; }
.layout-desktop .btnPlay:hover{ background-color:#009966; box-shadow: 0 0 0 1px rgba(31, 143, 74, 0.35) !important; }
.layout-desktop .btnPlay::after{ content: attr(title); margin: 0 10px 0 2px; color: black; }
.layout-desktop .material-icons.detailButton-icon.play_arrow{ color:black; }
.layout-desktop .btnPlay.detailButton{ flex-direction: row; }
.layout-desktop .listViewUserDataButtons{ display:none; }

/* ---------- Season list layout (desktop) ---------- */
.layout-desktop .vertical-list { 
    flex-direction: row; 
    flex-wrap: wrap; 
    justify-content: flex-start;
}
.layout-desktop .listItem-withContentWrapper { 
    align-items: flex-start; 
    flex-direction: column; 
    width: 24% !important;
    margin: 5px !important;
}
.layout-desktop .listItem-content { 
    display: flex; 
    width: 100%; 
    flex-direction: column; 
    align-items: flex-start; 
}
.layout-desktop .listItemImage-large { 
    height: 12vw !important;
    width: 100%; 
}

/* ---------- Card styling & hover effects ---------- */
.cardBox{
  background: rgba(0,0,0,.25);
  border-radius: 20px;
  box-shadow: 0 0 12px rgba(0,0,0,.4);
  transition: background .15s ease, box-shadow .25s ease, transform 0.25s ease-in-out !important;
  transform: translateZ(0); 
  backface-visibility: hidden;
}
.card:hover .cardBox { transform: translateY(-4px) !important; box-shadow: 0 8px 20px rgba(0, 0, 0, 0.28) !important; }
.card:active .cardBox { transform: translateY(-7px) !important; }
.cardScalable{ border-radius: 12px 12px 0 0; overflow:hidden; position:relative; }

/* Maintain ::after element for blur stack */
.cardImageContainer::after{
  content:"" !important; position:absolute !important; inset:0 !important;
  background: inherit !important; background-position: center !important; background-repeat: no-repeat !important;
  transform: none !important; transition: transform .18s ease !important; z-index:-1 !important;
}

/* Desktop card scaling on hover */
@media (hover: hover) and (pointer: fine){
  .cardImageContainer::after{ background-size: cover !important; }
  .card:hover .cardImageContainer::after{ transform: scale(1.08) !important; }
}

/* Mobile/touch card scaling */
@media (hover: none), (pointer: coarse), (max-width: 1068px){
  .cardImageContainer::after{ background-size: contain !important; transform: none !important; }
  .card:hover .cardImageContainer::after{ transform: none !important; }
}

/* Desktop hover blur layer */
.cardImageContainer::before{
  content:"" !important; position:absolute !important; inset:-10px !important;
  background: inherit !important; z-index:-2 !important;
  opacity: 0 !important; filter: none !important; transition: opacity .18s ease !important;
}
@media (hover: hover) and (pointer: fine){
  .card:hover .cardImageContainer::before{ opacity: 1 !important; filter: blur(10px) brightness(50%) !important; }
}
@media (hover: none), (pointer: coarse), (max-width: 1068px){
  .cardImageContainer::before{ opacity: 0 !important; filter: none !important; }
}

.cardImageContainer.defaultCardBackground{ background: transparent; }
.cardText{ padding: .6em .75em .8em; }
.cardText-primary{ font-weight:600; font-size:.95em; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
.cardText-secondary{ display:none; }

/* Remove bottom margins from cards */
.card, .cardBox, .cardContent, .verticalSection { margin-bottom: 0 !important; padding-bottom: 0 !important; }

/* Mobile horizontal scroll */
@media (max-width:1068px){
  .emby-scroller, .horizontalSection, .itemsContainer, .cardRow, .cardScalable{
    overflow-x:auto !important; -webkit-overflow-scrolling: touch;
  }
}

/* ---------- UI Extras ---------- */
.emby-tabs-slider .emby-tab-button{ color: rgba(255,255,255,.75); transition: color .2s ease; }
body.jsinject-requests-open .pageTitle{ display:none !important; }

/* Movies / Series toggle styling */
button.je-tab {
  font-family: "AaltoSansPro", "Aalto Sans Pro", system-ui, -apple-system, sans-serif !important;
  font-size: 1.1rem; padding: 10px 18px; min-height: 44px; font-weight: 600; letter-spacing: 0.4px; transition: all 0.15s ease;
}

/* ---------- Spotlight iframe wrapper ---------- */
#jsinject-spotlight-iframe-wrap{
  position: relative; width: calc(100% - 3rem); margin: 0 auto 1.5rem auto; border-radius: 18px; overflow: hidden; background:#000;
  box-shadow: 0 0 0 1px rgba(255,255,255,0.06), 0 12px 30px rgba(0,0,0,0.45);
}
#jsinject-spotlight-iframe{ display:block; width:100%; height:520px; border:0; transform: translateZ(0); }
@media (max-width:1000px){ #jsinject-spotlight-iframe{ height:360px; } }
@media (max-width:600px){
  #jsinject-spotlight-iframe-wrap{ width: calc(100% - 2rem); border-radius:14px; }
  #jsinject-spotlight-iframe{ height:70vh; }
}

/* ---------- Media bar text styling ---------- */
.separator-icon{ color:#00a450; }
.genre, .plot, .misc-info, .play-button, .runTime, .date{ font-family: "AaltoSansPro" !important; font-size: larger !important; }
.plot{ text-align:left; }

/* Play Button Tweaks */
.play-button .play-text{ position: relative; left: 4px; }
.play-button:hover .play-text, .play-button:focus-visible .play-text{ color:#fff !important; }
.play-button:hover::before, .play-button:focus-visible::before{ color:#fff !important; }

/* Watchlist & Favorites */
.slide .watchlist-button{ transition: color .2s; -webkit-tap-highlight-color: transparent; }
.favorite-button, .btnUserRating{ display:none !important; }
.watchlist-button:hover, .watchlist-button:focus-visible{ background-color:#cfcfcf !important; }

/* Visibility button icon replacement */
.je-detail-hide-btn .material-icons {
    color: transparent !important; display: flex !important; align-items: center; justify-content: center; position: relative; width: 24px; height: 24px;
}
.je-detail-hide-btn .material-icons::after {
    content: 'visibility_off'; font-family: 'Material Icons'; color: white; font-size: 24px !important; position: absolute; display: block; line-height: 1;
}

/* Request/Available Buttons */
button.jellyseerr-request-button.jellyseerr-button-request,
button[is="emby-button"].jellyseerr-request-button.jellyseerr-button-request,
button.jellyseer-button-request, .jellyseer-request-button, button.jellyseer-request {
  display: block !important; width: 100% !important; max-width: 320px !important; margin: 8px auto 0 !important; padding: 8px 14px !important; min-height: 36px !important;
  background: #4caf50 !important; border-color: #4caf50 !important; color: #fff !important; border-radius: 8px !important; font-size: 14px !important; font-weight: 600 !important;
  box-shadow: none !important; text-align: center !important; transition: background-color .2s ease, transform .15s ease !important;
}
button.jellyseerr-request-button.jellyseerr-button-request:hover,
button[is="emby-button"].jellyseerr-request-button.jellyseerr-button-request:hover,
button.jellyseer-button-request:hover, .jellyseer-request-button:hover, button.jellyseer-request:hover {
  background: #43a047 !important; border-color: #43a047 !important; color: #fff !important;
}
button.jellyseer-button-request:active, .jellyseer-request-button:active, button.jellyseer-request:active { background-color: #388e3c !important; }
button.jellyseer-button-request:focus { box-shadow: 0 0 0 2px rgba(76,175,80,.35) !important; }

button.jellyseerr-button-available {
  display: block !important; width: 100% !important; max-width: 320px !important; margin: 8px auto 0 !important; padding: 8px 14px !important;
  background: #1f8f4a !important; color: #fff !important; border-radius: 8px !important; font-size: 14px !important; font-weight: 600 !important;
  cursor: default !important; pointer-events: none !important; opacity: .9 !important;
}
button.jellyseerr-request-button svg, button.jellyseerr-button-available svg { margin-right: 6px !important; }

/* Jellyseerr modal primary button styling */
.jellyseerr-modal-button-primary { background: #1f8f4a !important; color: #ffffff !important; box-shadow: 0 4px 12px rgba(31, 143, 74, 0.35) !important; border: none !important; }
.jellyseerr-modal-button-primary:hover { background: #2ecc71 !important; }
.jellyseerr-media-badge-series { background-color: #4caf50 !important; }

/* ══════════════════════════════════════════════════════════
   Mobile Header Layout
   ══════════════════════════════════════════════════════════ */
@media (max-width: 900px) {

  :root {
    --htv-gutter: 14px;
    --htv-icon-gap: 12px;
    --htv-tab-h: 48px;
    --htv-tab-minw: 50px;
    --htv-icon-size: 24px;
    --htv-icon-dim: rgba(255, 255, 255, 0.85);
  }

  /* -------------------------------
     Global Gutters and Overflow
     ------------------------------- */
  .headerTop {
    padding-left: var(--htv-gutter) !important;
    padding-right: var(--htv-gutter) !important;
    display: flex !important;
    align-items: center !important;
    justify-content: space-between !important;
    flex-wrap: nowrap !important;
    overflow-x: auto !important; 
    scrollbar-width: none;
  }
  .headerTop::-webkit-scrollbar { display: none; }

  .headerTop .headerLeft,
  .headerTop .headerRight {
    display: flex !important;
    align-items: center !important;
    gap: var(--htv-icon-gap) !important;
    width: auto !important;
    flex-wrap: nowrap !important;
    flex-shrink: 0 !important;
  }

  /* -------------------------------
     Disable auto-hide header on scroll
     ------------------------------- */
  .headerTop.hide, .skinHeader.hide,
  .headerTop.hidden, .skinHeader.hidden {
    display: flex !important;
    opacity: 1 !important;
    transform: none !important;
    pointer-events: auto !important;
  }
  
  /* Mobile scroll lock fix */
  body, .skinBody, .mainAnimatedPages {
    overflow-y:auto !important;
    touch-action: pan-y !important;
    overscroll-behavior:auto !important;
  }

  /* -------------------------------
     Menu Button Visibility
     ------------------------------- */
  .headerTop .headerLeft {
    position: relative !important;
    z-index: 50 !important;
  }

  .barsMenuButton,
  .mainDrawerButton {
    display: inline-flex !important;
    align-items: center !important;
    justify-content: center !important;
    min-width: 44px !important;
    width: 44px !important;
    min-height: 44px !important;
    padding: 8px !important;
    margin: 0 !important;
    flex-shrink: 0 !important;
    opacity: 1 !important;
    visibility: visible !important;
    transform: none !important;
    z-index: 60 !important;
  }

  .barsMenuButton .material-icons, .barsMenuButton .md-icon, .barsMenuButton i, .barsMenuButton svg,
  .mainDrawerButton .material-icons, .mainDrawerButton .md-icon, .mainDrawerButton i, .mainDrawerButton svg {
    opacity: 1 !important;
    visibility: visible !important;
    color: #ffffff !important;
    filter: none !important;
  }

  /* -------------------------------
     Hide Logo and Random Button
     ------------------------------- */
  .headerTop .headerLeft .headerLogo,
  .headerTop .headerLeft .pageTitleWithDefaultLogo,
  .headerTop .headerLeft .headerHomeButton,
  .headerTop .headerLeft button[title="Home"],
  #randomItemButtonContainer,
  #randomItemButtonContainer button,
  button#randomItemButton,
  button[title*="random item" i],
  button[title*="random" i] {
    display: none !important;
  }

  /* -------------------------------
     Top Tab Bar Icons Layout
     ------------------------------- */
  .headerTabs.sectionTabs .emby-tabs,
  .headerTabs.sectionTabs .emby-tabs-slider{ overflow-x:hidden !important; }
  .headerTabs.sectionTabs .emby-tabs{ width:100% !important; }
  
  .headerTabs.sectionTabs .emby-tabs-slider {
    display:flex !important; align-items:center !important; justify-content: space-between !important;
    width:100% !important; max-width:100% !important; white-space: nowrap !important; padding: 0 10px !important;
    box-sizing: border-box !important; transform:none !important;
  }
  
  .headerTabs.sectionTabs .emby-tab-button {
    display: none !important;
  }

  /* Specific Tab IDs and Watchlist */
  .headerTabs.sectionTabs .emby-tab-button[data-index="0"],
  .headerTabs.sectionTabs #htv-tab-search,
  .headerTabs.sectionTabs #customTabButton_0,
  .headerTabs.sectionTabs #htv-tab-request,
  .headerTabs.sectionTabs #htv-tab-stream {
    display: inline-flex !important;
    font-size: 0 !important;
    color: transparent !important;
    position: relative !important;
    min-width: var(--htv-tab-minw) !important;
    height: var(--htv-tab-h) !important;
    background: transparent !important;
    margin: 0 !important;
    padding: 0 !important;
    flex: 1 1 0 !important; 
    align-items: center !important;
    justify-content: center !important;
  }

  .headerTabs.sectionTabs .emby-tab-button::after {
    font-family: 'Material Icons' !important;
    font-size: var(--htv-icon-size) !important;
    color: var(--htv-icon-dim) !important;
    content: "" !important;
    display: block !important;
    position: absolute !important;
    left: 50% !important;
    top: 50% !important;
    transform: translate(-50%, -50%) !important;
    visibility: visible !important;
    pointer-events: none !important;
  }

  .headerTabs.sectionTabs .emby-tab-button-active::after {
    color: #ffffff !important;
  }

  /* Icon mapping with updated IDs */
  .headerTabs.sectionTabs .emby-tab-button[data-index="0"]::after { content: "home" !important; }
  #htv-tab-search::after { content: "search" !important; }
  #customTabButton_0::after { content: "bookmark_border" !important; }
  #htv-tab-request::after { content: "add_circle_outline" !important; }
  #htv-tab-stream::after { content: "play_circle_outline" !important; }

  /* Hide tab text */
  #htv-tab-search .buttonText,
  #htv-tab-request .buttonText,
  #htv-tab-stream .buttonText,
  #customTabButton_0 .emby-button-foreground,
  #customTabButton_0 .buttonText {
    display: none !important;
  }

  /* -------------------------------
     Header Icons Styling
     ------------------------------- */
  .headerTop .material-icons,
  .headerTop .headerButton {
    color: #ffffff !important;
    opacity: 1 !important;
    flex-shrink: 0 !important;
  }

  .headerButton .buttonText {
    display: none !important;
  }
  
  /* -------------------------------
     BACK BUTTON RULE
     ------------------------------- */
  .material-icons.chevron_left {
    display: inline-block !important;
    opacity: 1 !important;
    visibility: visible !important;
  }

  .homePage .material-icons.chevron_left,
  .page-home .material-icons.chevron_left,
  body[data-routename="home"] .material-icons.chevron_left,
  .skinBody[data-routename="home"] .material-icons.chevron_left,
  #homePage .material-icons.chevron_left {
    display: none !important;
  }
  
  /* -------------------------------
     MOBILE PLAY BUTTON & ICONS
     ------------------------------- */
  .play-button .play-text{ display:none !important; }
  .play-button{
    display:inline-flex !important; align-items:center !important; justify-content:center !important;
    padding:8px 12px !important; min-width:0 !important; gap:0 !important;
  }
  .play-button::before, .play-button::after{ margin:0 !important; }
  .play-button i, .play-button svg{ margin:0 !important; display:inline-block !important; vertical-align: middle !important; }
  
  button.btnPlay .play_arrow, 
  button[data-action="resume"] .play_arrow,
  .detailButton-icon.play_arrow {
      color: #ffffff !important; -webkit-text-fill-color: #ffffff !important; opacity: 1 !important;
  }
} 
/* ══════════════════════════════════════════════════════════
   END MOBILE HEADER
   ══════════════════════════════════════════════════════════ */


/* ══════════════════════════════════════════════════════════
   Detail Page Layout
   ══════════════════════════════════════════════════════════ */

/* Primary container (Base) */
.detailPagePrimaryContainer {
    background-color: var(--transparent-bg);
    z-index: 2;
}

/* Desktop Detail Page Alignment */
.layout-desktop .detailPagePrimaryContainer {
    display: inline-table;
    position: relative;
    left: 5%;
    padding-left: 5%;
}

.layout-desktop .detailPageContent {
    padding-left: 5%;
}

.layout-desktop .detailPagePrimaryContent {
    padding-left: 0;
    width: 95% !important; 
}

.layout-desktop .infoWrapper, 
.layout-desktop .mainDetailButtons {
    padding-left: 0; /* Set to 0 to align perfectly with the plot and track selections below */
    max-width: 45%; 
}

.layout-desktop .detailPageSecondaryContainer {
    background-color: var(--transparent-bg);
    margin: 0 0 1.25em 0;
}

.layout-desktop .itemDetailsGroup {
    margin-top: 0;
}

html.preload.layout-desktop {
    background-color: #181818;
}

.layout-desktop .detailLogo {
    display: none !important;
}

/* Desktop Backdrop Alignment */
.layout-desktop .itemBackdrop {
    background-attachment: scroll;
    background-position: center;
    background-repeat: no-repeat;
    background-size: cover;
    height: 100vh;
    position: relative;
    right: -20%;
    width: 80vw;
}

.backdropContainer {
    height: 100vh;
    left: 20%;
    position: absolute;
    width: 80vw;
}

/* Detail Page Gradient Background */
#reactRoot:has(.skinHeader.semiTransparent) > .backgroundContainer.withBackdrop,
#reactRoot > .skinHeader.semiTransparent + .backgroundContainer.withBackdrop {
    background-color: transparent;
    background:
        linear-gradient(90deg, var(--bg-color), rgba(24, 24, 24, 0) 60%),
        linear-gradient(360deg, var(--bg-color), rgba(24, 24, 24, 0) 60%);
    background-size: cover;
    height: 100vh;
    margin-left: 20%;
    position: absolute;
    width: 80vw;
}

.layout-desktop .backgroundContainer {
    background:
        linear-gradient(90deg, var(--bg-color), rgba(24, 24, 24, 0) 60%),
        linear-gradient(360deg, var(--bg-color), rgba(24, 24, 24, 0) 60%);
    background-size: cover;
    height: 100vh;
    margin-left: 20%;
    position: absolute;
    width: 80vw;
}

.layout-desktop .detailPageWrapperContainer {
    margin-top: -65vh;
}

.skinHeader:has(.noHomeButtonHeader) .backdropContainer,
.skinHeader.noHomeButtonHeader .backdropContainer {
    backdrop-filter: blur(16px) saturate(180%);
    -webkit-backdrop-filter: blur(16px) saturate(180%);
    background-color: #18181849;
    width: 100vw;
    left: 0;
}

.skinHeader:has(.noHomeButtonHeader) .layout-desktop .backgroundContainer,
.skinHeader.noHomeButtonHeader .layout-desktop .backgroundContainer {
    display: none;
}

#reactRoot:not(:has(.skinHeader.semiTransparent)) > .backgroundContainer.withBackdrop {
    position: fixed !important;
    overflow: visible !important;
    background: none;
    background-color: #18181849;
    margin-left: 0;
    background-size: cover;
    width: 100vw;
    left: 0;
    top: 0;
    height: 100vh;
    z-index: 0;
}

#reactRoot:not(:has(.skinHeader.semiTransparent)) > .backgroundContainer.withBackdrop::before {
    content: '';
    position: absolute;
    top: -40px;
    right: -40px;
    bottom: -40px;
    left: -40px;
    pointer-events: none;
    backdrop-filter: blur(30px) saturate(80%) brightness(60%);
    -webkit-backdrop-filter: blur(30px) saturate(80%) brightness(60%);
    background: none;
    background-color: #18181849;
    z-index: -1;
}

#reactRoot:not(:has(.skinHeader.semiTransparent)) > .backdropContainer > .backdropImage {
    animation-name: parralax;
    animation-duration: 1ms;
    animation-direction: alternate;
    animation-timeline: scroll(block nearest);
}

@keyframes parralax {
    from { transform: translateY(120px); }
    to { transform: translateY(-120px); }
}

#reactRoot:not(:has(.skinHeader.semiTransparent)) > .backdropContainer {
    height: 100vh;
    left: 0;
    position: fixed !important;
    width: 100vw;
    animation: BackgroundMove 100s linear infinite; 
    will-change: transform; 
}

@keyframes BackgroundMove {
    0% { transform: translate3d(0, 0, 0); }
    50% { transform: translate3d(-2%, 0, 0); } 
    100% { transform: translate3d(0, 0, 0); }
}

/* Hide poster card & tags on desktop detail page */
.layout-desktop .detailImageContainer .card,
.layout-desktop .itemTags,
.layout-desktop .itemName.originalTitle {
    display: none;
}

.layout-desktop .itemName,
.layout-mobile .itemName {
    font-weight: normal;
}

.layout-desktop a.overview-expand.emby-button {
    margin: 0;
    padding: 0;
}

.layout-desktop [dir=ltr] .detailRibbon,
.layout-tv [dir=ltr] .detailRibbon {
    display: contents;
    padding-left: 0;
    background: #00000000;
}

.noBackdropTransparency .detailPagePrimaryContainer {
    background-color: #00000000;
}

/* Item Title Styling */
.itemDetailPageTitle,
.nameContainer h1,
h1.itemName {
    font-size: clamp(2.2rem, 5.5vw, 4.8rem) !important;
    font-weight: 900 !important;
    letter-spacing: -0.01em !important;
    line-height: 1.04 !important;
    color: #ffffff !important;
    text-shadow: 0 2px 28px rgba(0, 0, 0, 0.85) !important;
}

/* Mobile Detail Page Layout */
.layout-mobile .detailPagePrimaryContainer {
    display: block !important;
    position: relative !important;
    left: 0 !important;
    margin: 0 !important;
    padding: 0 !important;
    width: 100vw !important;
    max-width: 100% !important;
    box-sizing: border-box !important;
    background-color: var(--bg-color) !important;
}

.layout-mobile .detailPageContent,
.layout-mobile [dir=ltr] .infoWrapper,
.layout-mobile .mainDetailButtons {
    padding-left: 5vw !important;
    padding-right: 5vw !important;
    margin: 0 !important;
    width: 100% !important;
    max-width: 100% !important;
    box-sizing: border-box !important;
}

/* Reset Container Shifts */
.layout-mobile .detailRibbon,
.layout-mobile .trackSelections,
.layout-mobile .itemDetailsGroup,
.layout-mobile .detailPageSecondaryContainer {
    width: 100% !important;
    padding-left: 5vw !important;
    padding-right: 5vw !important;
    margin: 0 !important;
    right: 0 !important;
    position: static !important;
    box-sizing: border-box !important;
}

.layout-mobile .detailImageContainer .card {
    display: none;
}

.layout-mobile :has(.backgroundContainer:not(.withBackdrop)) .detailImageContainer .card:has(.cardImageIcon.person) {
    left: 35vw;
    top: -15rem; 
    margin-bottom: 1rem;
    border-radius: 8px; 
}

/* ==========================================================
   Actor Card Layout for Cast Pages
   ========================================================== */

/* Actor Image Card Alignment */
html body:has([data-type="Person"]) .layout-desktop .detailImageContainer .card,
html body .detailImageContainer .card:has(.cardImageIcon.person) {
    display: block !important;
    position: relative !important;
    left: 0 !important;
    top: 0 !important;
    width: 300px !important;
    max-width: 30vw !important;
    border-radius: 12px !important;
    margin-right: 2rem !important; 
    box-shadow: 0 8px 20px rgba(0,0,0,0.4) !important;
}

/* Detail Container Flex Layout */
html body:has([data-type="Person"]) .layout-desktop .detailPagePrimaryContainer {
    display: flex !important;
    flex-direction: row !important;
    align-items: flex-start !important;
    position: relative !important;
    margin-top: 5vh !important;
    padding-left: 5vw !important;
    background: transparent !important;
}

/* Bio Text Width */
html body:has([data-type="Person"]) .layout-desktop .detailPagePrimaryContent {
    width: auto !important;
    flex: 1 !important;
    padding-left: 0 !important;
}

/* Info Ribbon Alignment */
html body:has([data-type="Person"]) .layout-desktop [dir=ltr] .detailRibbon {
    display: flex !important;
    flex-direction: row !important;
    align-items: center !important;
    background: transparent !important;
}

/* =====================================================
   Login Page Layout
   ===================================================== */
body:has(#loginPage) .barsMenuButton,
body:has(#loginPage) .mainDrawerButton,
body:has(#loginPage) #htv-lowdata-btn {
    display: none !important;
}

/* ==========================================================
   Floating Header Overrides
   ========================================================== */
/* Base State: Top of the page */
.skinHeader {
  width: 100% !important;
  left: 50% !important;
  transform: translateX(-50%) !important;
  margin: 0 !important;
  border-radius: 0 !important;
  box-shadow: none !important;
  background: transparent !important;
  transition: width 0.3s ease, margin 0.3s ease, border-radius 0.3s ease, box-shadow 0.3s ease, background 0.3s ease, backdrop-filter 0.3s ease !important;
}

/* Scrolled State: Triggered by our Custom JS */
.skinHeader.htv-pill-active {
  width: 99% !important;
  margin: 7px 0px !important;
  border-radius: 50px !important;
  box-shadow: 2px 2px 2px -3px rgba(255, 255, 255, 0.65) inset, -2px -2px 2px -3px rgba(255, 255, 255, 0.61) inset, 0px 0px 15px rgba(0, 0, 0, 0.329) !important;
  background: #f0f8ff05 !important;
  backdrop-filter: blur(12px) !important;
  -webkit-backdrop-filter: blur(12px) !important;
}

/* Fix for unclickable links */
.headerTop, .headerTabs, .emby-tabs-slider {
    overflow: visible !important; 
}

.emby-tab-button {
    pointer-events: auto !important; 
}

/* Header Pointer Events */
.skinHeader {
    pointer-events: none;
}
.skinHeader > * {
    pointer-events: auto;
}

/* ==========================================================
   Mobile Adjustments (< 768px)
   ========================================================== */
@media screen and (max-width: 767px) {
  
  /* Card Transformations */
  .cardBox {
      transform: none !important;
      backface-visibility: visible !important;
      box-shadow: 0 4px 8px rgba(0, 0, 0, 0.3) !important; 
      transition: none !important; 
  }

  /* Disable Background Animations */
  #reactRoot:not(:has(.skinHeader.semiTransparent)) > .backdropContainer {
      animation: none !important;
      will-change: auto !important;
  }
  #reactRoot:not(:has(.skinHeader.semiTransparent)) > .backdropContainer > .backdropImage {
      animation: none !important;
  }

  /* Backdrop Filters */
  .skinHeader:has(.noHomeButtonHeader) .backdropContainer,
  .skinHeader.noHomeButtonHeader .backdropContainer,
  #reactRoot:not(:has(.skinHeader.semiTransparent)) > .backgroundContainer.withBackdrop::before,
  .skinHeader.htv-pill-active {
      backdrop-filter: none !important;
      -webkit-backdrop-filter: none !important;
  }
  
  /* Background Fallbacks */
  .skinHeader.htv-pill-active {
      background: rgba(24, 24, 24, 0.95) !important; 
  }
  #reactRoot:not(:has(.skinHeader.semiTransparent)) > .backgroundContainer.withBackdrop::before {
      background-color: rgba(24, 24, 24, 0.8) !important;
  }
}