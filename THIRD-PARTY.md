# Сторонні матеріали

Код застосунку віддано в суспільне надбання — див. [`LICENSE`](LICENSE) (The Unlicense): беріть, змінюйте,
продавайте, жодних гарантій.

Файли, перелічені нижче, написані не мною, і я не маю права перелицензовувати їх: кожен лишається під
ліцензією своїх авторів.

| Файл у репозиторії | Джерело | Ліцензія | Що змінено |
|---|---|---|---|
| `KeySwitcher.Core/Resources/Dictionaries/uk.txt` | [LibreOffice/dictionaries](https://github.com/LibreOffice/dictionaries) (тека `uk_UA`, на основі [brown-uk/dict_uk](https://github.com/brown-uk/dict_uk)) | **MPL 1.1** | з `uk_UA.dic` витягнуто список базових форм у текстовий файл — по слову в рядку |
| `KeySwitcher.Core/Resources/Dictionaries/en.txt` | [dwyl/english-words](https://github.com/dwyl/english-words) | **The Unlicense** (суспільне надбання) | нічого, файл узято як є |
| [Serilog](https://serilog.net/) + `Serilog.Sinks.File` | NuGet | Apache-2.0 | нічого, підключається як залежність |

> Ліцензії взято з першоджерел: `uk.txt` — з `README_uk_UA.txt` у репозиторії LibreOffice
> («This dictionary is licensed under MPL (Mozilla Public License) 1.1 license»), `en.txt` — з `LICENSE.md`
> у dwyl/english-words (текст The Unlicense).

## Повідомлення для `uk.txt` (MPL 1.1, Exhibit A)

The contents of this file are subject to the Mozilla Public License Version 1.1 (the "License"); you may not
use this file except in compliance with the License. You may obtain a copy of the License at
<http://www.mozilla.org/MPL/>

Software distributed under the License is distributed on an "AS IS" basis, WITHOUT WARRANTY OF ANY KIND,
either express or implied. See the License for the specific language governing rights and limitations under
the License.

The Original Code is the Ukrainian spelling dictionary from the LibreOffice `uk_UA` dictionary set, based on
the dict_uk project (<https://github.com/brown-uk/dict_uk>).

The Initial Developer of the Original Code is Andriy Rysin `<arysin@gmail.com>` and the dict_uk contributors.
Portions created by the Initial Developer are Copyright (C) 2007–2017. All Rights Reserved.

Modifications: the word list was extracted from `uk_UA.dic` into a plain text file (`uk.txt`, one word per
line) so it can be embedded as a resource.

Contributor(s): KeySwitcher — the extraction described above.

Повний текст ліцензії — у [`licenses/MPL-1.1.txt`](licenses/MPL-1.1.txt).
