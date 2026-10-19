# Кремінь: візуальний ресерч і напрями для малювання

Дата: 2026-10-18. Це артреференс, не готовий атлас і не заміна предметів у грі.

## Реальний матеріал

Кремінь (flint), не хімічний елемент кремній: різновид кременистої породи chert, часто у вигляді конкрецій. BGS описує сірий/чорний кремінь у крейді. Для малювання корисне поєднання темної серцевини зі світлою, нерівномірною зовнішньою кіркою. Свіжий злам може бути гладким, вигнутим, мушлеподібним, із гострою кромкою; це не кристал із регулярними гранями. Є й бурі різновиди. Обкатані уламки можуть бути округлими, тому для впізнавання варто залишити один помітний відкритий скол.

Джерела:
- British Geological Survey, визначення flint: https://webapps.bgs.ac.uk/bgsrcs/rcs_details.cfm?code=FLNT
- Ohio Department of Natural Resources, твердість/крихкість/мушлеподібний злам: https://ohiodnr.gov/discover-and-learn/rock-minerals-fossils/minerals/flint
- Фото реальних прибережних конкрецій з кіркою (продавець зразків, фотографічний референс, не геологічна класифікація): https://nativewayonline.com/products/english-flint-nodule-specimens-with-cortex

## Приклади з ігор

Minecraft: компактний темний піксельний уламок із контрастними сірими площинами — ставка на читабельний силует. Офіційна стаття з ігровими зображеннями: https://www.minecraft.net/en-us/article/taking-inventory--flint
Додатковий візуальний референс 16x16 іконки (фанатський pixel/bead pattern): https://laula-beads.hatenablog.com/entry/2021/08/11/115228

Valheim: світлий сірувато-білий гладкий камінець уздовж води. Хороший приклад відмінності pickup-предмета від темнішого звичайного каменю, але просте копіювання світлого силуету може губитися на нашому піску. Скриншоти і опис: https://www.pcgamer.com/valheim-flint-guide/

Don't Starve: приклад виразної обведеної стилізації предмета на землі; скриншот взаємодії на форумі розробника: https://forums.kleientertainment.com/klei-bug-tracker/dont-starve-together/leftright-click-mixed-up-r1296/

Це вибрані приклади, не твердження про єдиний загальноприйнятий дизайн в усіх іграх.

## Наш аркуш

References/Flint-Concept-Board-2026-10-18.png

Чотири колонки; верхній ряд — інвентар, середній — малий World-спрайт на піску, нижній — такий самий на темному камені. Це згенеровані ілюстративні макети, не скриншоти запущеної гри.

A — темна серцевина й світла кірка, асиметрична конкреція. Основна рекомендація.
B — гострий холодний уламок. Добре читається, але вже схожий на оброблений наконечник.
C — обкатана галька з одним великим сколом. Найприродніший прибережний варіант, трохи слабше відрізняється від звичайного каменю.
D — теплий бурий уламок. Можливий природний варіант; для нашої сцени сильніше зближується за кольором із ґрунтом.

Моя художня пропозиція: силует A, трохи пом'якшений як C. Не суцільна світла обводка, а пляма кірки на частині краю. Дві-три великі площини замість шуму. Без іскор або магічного сяйва на звичайному предметі.

Орієнтир для ручного малювання, не вимога до поточного генератора: inventory canvas 32x32 із силуетом близько 20–26 px; World силует близько 14–18 px завширшки і 8–12 px заввишки, із нижчим ракурсом та простішими деталями. Остаточно перевірити при реальному масштабі камери, на піску, скелях і вночі. Обидва зображення мають залишати однакову впізнавану світлу пляму/виїмку.

Пропоновані стартові кольори (не автоматично витягнута й не суворо перевірена палітра існуючих текстур):
- контур: #242522
- темна серцевина: #303B3D
- середня площина: #4B595B
- світлий скол: #75807A
- стара кірка: #AAA994
- світла кірка: #D0C6A9

Реалістична примітка для механіки: традиційні джерела іскор — кремінь зі сталлю або піритом; два кремені самі по собі не є еквівалентом такого набору. Це не пропозиція змінити вже погоджену ігрову механіку. Джерело: офіційна стаття Minecraft вище, абзац про real-world flint.

## Генерація

Використано вбудований image generation, не CLI. Три локальні зображення використано лише як style/palette references: torch_icon.png, nigredo_icon.png та скриншот узбережжя. Ігрові assets і посилання не змінювалися. Аркуш — концепт; його роздільність, палітра та піксельна сітка не є валідованими production sprites.

Точний prompt:

Use case: stylized-concept.
Asset type: ONE art-direction comparison board for original pixel-art FLINT items in an indie dark-fantasy top-down game. This is a brainstorming sheet, NOT a final game sprite atlas or a screenshot.
Reference images: Image 1 (torch icon) and Image 2 (Nigredo hand/stone icon) are STYLE ONLY: coarse hand-clustered pixels, restrained earthy colors, dark thin outlines, not the subjects to copy. Image 3 (game shoreline screenshot) is WORLD PALETTE and CAMERA REFERENCE ONLY. Do not edit the images and do not copy their UI/character.
Primary request: A clear landscape comparison board with FOUR COLUMNS A, B, C, D and THREE ROWS. Each column represents the same flint design across its inventory icon and two world tests. Top row INVENTORY: enlarged isolated single-piece icon on a dark charcoal slot. Middle row SAND: a noticeably smaller same stone lying flat on a simple muted olive-beige sandy patch. Bottom row ROCK: the identical small world sprite on a muted dark blue-grey rocky patch. Make all three rows equally legible, excellent spacing, consistent scale across columns. The world stones are lower/flatter in perspective than the inventory icons and retain the same identifying silhouette/rind/notch. Flat overhead-oblique camera, NOT a tall isometric cube.
Four distinct designs:
A CHALK EDGE: squat asymmetrical broken natural nodule, dark charcoal flint exposed in a large smooth concave shell-shaped fracture, substantial discontinuous chalky cream outer cortex along one side and base, a small angular notch. Best balanced option.
B SHARP FLAKE: thin broad asymmetrical chipped wedge, cold slate-grey dark core, 2-3 bold curved flake scars, pointed but NOT a symmetrical arrowhead, NO spearhead/no shaft/no crafting. Pale narrow chip rim, clear dark outline.
C BEACH PEBBLE: naturally water-worn rounded oval pebble, slightly flattened, light grey-beige weathered outside with ONE large fresh dark broken face. Most natural shoreline find, visibly different from a generic round rock.
D BROWN FLINT: compact irregular warm smoky-brown flint shard, beige weathered rind, two broad curved fracture planes, restrained amber-brown not metallic gold. A warmer alternative, not a crystal.
Style: deliberately LOW-RES pixel-art sprite concepts with chunky square clusters and crisp nearest-neighbour-like enlarged edges, NOT painted high-resolution illustrations. Inventory silhouettes approximately a 24x24 logical pixel drawing within a 32x32 logical canvas. World silhouettes approximately 14x10 to 18x12 logical pixels, visibly simpler. Do not claim exact pixel output sizes. 5-7 flat colors per stone, no gradients, no blur, no antialiasing, no tiny realistic grain. Use dark outline #242522, core #303b3d / #4b595b, midplane #75807a, cortex #aaa994 / #d0c6a9; D may add warm #625044 / #947d61. Similar muted subdued contrast to the references, but visible on sand and dark stone without glow. Thin contact shadow only in world samples. No spark effects, no fire, no magical glow, no hands, no tools, no polished gems, no photorealism. No game branding.
Layout text only: small title "FLINT STUDIES"; column labels exactly "A  CHALK EDGE", "B  SHARP FLAKE", "C  BEACH PEBBLE", "D  BROWN FLINT"; row labels "INVENTORY", "SAND", "ROCK". Small understated swatch strip at bottom optional. Dark neutral editorial board, restrained thin separators, art is the focus. No other text.
