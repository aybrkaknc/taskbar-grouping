<a id="readme-top"></a>

[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![CI](https://github.com/aybrkaknc/taskbar-grouping/actions/workflows/ci.yml/badge.svg)](https://github.com/aybrkaknc/taskbar-grouping/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

<br />
<div align="center">
  <h3 align="center">TaskbarFolders</h3>

  <p align="center">
    Windows 11 görev çubuğu için macOS tarzı uygulama klasörleri. İlgili uygulamaları tek bir sabitlenmiş kutucuk arkasında gruplayın; kutucuğa tıkladığınızda uygulamalar kutucuğa bağlı bir açılır pencerede görünür.
    <br />
    <a href="https://github.com/aybrkaknc/taskbar-grouping"><strong>Depoyu keşfedin</strong></a>
    <br />
    <br />
    <a href="https://github.com/aybrkaknc/taskbar-grouping/issues/new?labels=bug">Hata Bildir</a>
    &middot;
    <a href="https://github.com/aybrkaknc/taskbar-grouping/issues/new?labels=enhancement">Özellik İste</a>
  </p>

  <p align="center">
    <a href="README.md">English</a> | <a href="README.tr.md">Türkçe</a>
  </p>
</div>

> **Bu bir fork'tur.** [gianluca-schwekendiek/taskbar-grouping](https://github.com/gianluca-schwekendiek/taskbar-grouping) projesinden bağımsız olarak geliştirilmektedir. Devam eden çalışmalar [`feature/send-to-group`](https://github.com/aybrkaknc/taskbar-grouping/tree/feature/send-to-group) dalında yer alır ve henüz yayımlanmamıştır. Eklenenler:
>
> - Gruplar yeniden adlandırılabilir; yeniden adlandırma artık eski bir Başlat menüsü girdisi bırakmaz (upstream #25, #28);
> - Grup adı, kapat, uygulama ekle, düzenleme modu ve **açık tut (pin-open)** düğmeleri içeren, cam efektli ve solma geçişli bir açılır pencere başlığı;
> - Bir programı veya kısayolu bir gruba göndermek için Explorer'ın sağ tık menüsünde **Taskbar Groups** seçeneği;
> - Sabitlenmiş kutucuğun simgesi grubun uygulamalarını izler, kutucuğa ikinci tıklama açılır pencereyi kapatır ve büyük gruplar kaydırılabilir (upstream #32);
> - Kullanıcı bazlı bir kurulum programı ve geride hiçbir şey bırakmayan bir kaldırma programı.
>
> Ayrıntılar için dalın [CHANGELOG](https://github.com/aybrkaknc/taskbar-grouping/blob/feature/send-to-group/CHANGELOG.md) dosyasına bakın. Aşağıdaki sürümler ve indirmeler hâlâ upstream projeye aittir.

<details>
  <summary>İçindekiler</summary>
  <ol>
    <li><a href="#proje-hakkında">Proje Hakkında</a></li>
    <li><a href="#gereksinimler">Gereksinimler</a></li>
    <li><a href="#kurulum">Kurulum</a></li>
    <li><a href="#hızlı-başlangıç">Hızlı Başlangıç</a></li>
    <li><a href="#nasıl-çalışır">Nasıl Çalışır</a></li>
    <li><a href="#ayarlar">Ayarlar</a></li>
    <li><a href="#kaynaktan-derleme">Kaynaktan Derleme</a></li>
    <li><a href="#belgeler">Belgeler</a></li>
    <li><a href="#katkıda-bulunma">Katkıda Bulunma</a></li>
    <li><a href="#lisans">Lisans</a></li>
  </ol>
</details>

## Proje Hakkında

<p align="center">
  <img src="assets/screenshots/tbgroup.gif" alt="Sabitlenmiş bir grup kutucuğuna tıklamak açılır pencereyi açar ve bir uygulamayı başlatır" width="420">
</p>

* **Tek bir görev çubuğu kutucuğunun arkasında uygulama grupları.** Grubun içine `.exe` veya `.lnk` dosyalarını sürükleyin; grup kendi sabitlenebilir kısayoluna sahip olur.
* **Birleşik grup simgeleri**: Simge, gruptaki uygulamaların simgelerinden üretilir.
* **Tek tıkla sabitleme.** Yönetici, grubu Windows'a sabitlemesini ister. Sürümünüz veya politikanız programatik sabitlemeyi engelliyorsa, kısayol klasörünü açar ve sabitlemeyi elle yapabilirsiniz.
* **Windows'a benzeyen açılır pencere**: Mica, Mica Alt, Akrilik veya düz bir panel; isteğe bağlı renk tonu. Odağı kaybettiğinde ya da bir şey başlattığınızda kapanır.
* **Windows temasını izler**: Açık, koyu veya sistem teması.
* **Çoklu monitör ve yüksek DPI uyumlu.**

**Kapsam dışı:** Görev çubuğu değiştirici veya kabuk eklentisi, arama ya da kısayol tuşları olan bir başlatıcı ve her Windows sürümüyle tam uyumluluk (bkz. [Gereksinimler](#gereksinimler)).

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

## Gereksinimler

| | |
|---|---|
| **İşletim sistemi** | Windows 11 önerilir. Kurulum programı Windows 10 1809 ve sonrasına izin verir. |
| **.NET çalışma zamanı** | Gerekmez. Sürümler bağımsızdır. |
| **Mimari** | x64. ARM64 derlemesi yoktur. |

* **Tek tıkla sabitleme** için Windows 10 2004 (derleme 19041) veya üstü ve programatik sabitlemeye izin veren bir sürüm gerekir. Aksi halde yönetici, elle sabitleme için kısayol klasörünü açar.
* **Mica arka planı** için Windows 11 22H2 veya üstü gerekir. Eski derlemelerde düz arka plan kullanılır.

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

## Kurulum

1. [Releases](https://github.com/gianluca-schwekendiek/taskbar-grouping/releases) sayfasından `TaskbarFolders-Setup.exe` dosyasını indirin. Kurulum, `%LOCALAPPDATA%\Programs\TaskbarFolders` klasörüne kullanıcı bazında kurulur ve yönetici hakkı gerektirmez.
2. Başlat menüsünden **TaskbarFolders Manager** uygulamasını açın.

**Taşınabilir sürüm:** `TaskbarFolders-portable.zip` dosyasını indirin, çıkarın, `Manager` ve `Launcher` klasörlerini yan yana tutun ve `Manager\TaskbarFolders.Manager.exe` dosyasını çalıştırın.

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

## Hızlı Başlangıç

1. **TaskbarFolders Manager** uygulamasını açın.
2. **+ New group** düğmesine tıklayın, bir ad yazın ve `Enter` tuşuna basın.
3. `.exe` veya `.lnk` dosyalarını **Apps** sekmesine bırakın ya da **Add apps...** seçeneğini kullanın.
4. **Pin to taskbar** düğmesine tıklayın. Windows onay ister.
5. Yeni görev çubuğu kutucuğuna tıklayın ve açılır pencerede bir simgeye tıklayarak uygulamayı başlatın.

Uygulaması olmayan bir grup simge ve kısayol üretmez, bu yüzden sabitlemeden önce en az bir uygulama ekleyin.

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

## Nasıl Çalışır

Her grubun `%APPDATA%\TaskbarFolders\shortcuts\` klasöründe kendi `.lnk` dosyası vardır. Tüm kısayollar ortak bir `TaskbarFolders.Launcher.exe` dosyasını gösterir, `--group-id` argümanı ve ayrı bir AppUserModelID (`TaskbarFolders.Group.<id>`) taşır. Böylece Windows her grubu bağımsız bir görev çubuğu kutucuğu olarak gösterir.

Windows programatik sabitlemeyi yalnızca AppUserModelID Başlat menüsü dizininde biliniyorsa kalıcı tutar. Bu nedenle eşleşen bir kısayol Başlat menüsüne de yazılır.

Ayrıntılar: [docs/architecture.md](docs/architecture.md) ve [ADR-002](docs/adr/002-per-group-lnk-aumid.md).

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

## Ayarlar

| Ayar | Seçenekler | Varsayılan |
|---|---|---|
| Tema | Sistem, Açık, Koyu | Sistem |
| Açılır pencere konumu | Otomatik, Üstte, Altta | Otomatik |
| Açılır pencere arka planı | Mica, Mica Alt, Akrilik, Kapalı | Mica |
| Açılır pencere tonu | Yok, Vurgu, Gece, Orman, Gün batımı | Yok |
| Açılır pencere animasyonları | açık / kapalı | açık |
| Windows açıldığında TaskbarFolders Manager'ı başlat | açık / kapalı | kapalı |
| Explorer sağ tık menüsünde "Taskbar Groups" göster | açık / kapalı | açık |

Veriler `%APPDATA%\TaskbarFolders\` altında tutulur (gruplar, simgeler, kısayollar, ayarlar ve günlükler). Kaldırma bu klasörü siler; gruplarınızı korumak istiyorsanız önce `groups\` klasörünü yedekleyin. Sorun giderme: [docs/troubleshooting.md](docs/troubleshooting.md).

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

## Kaynaktan Derleme

Windows üzerinde [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) gerekir.

```sh
git clone https://github.com/aybrkaknc/taskbar-grouping.git
cd taskbar-grouping
dotnet build TaskbarFolders.sln -c Release
dotnet test TaskbarFolders.sln -c Release
dotnet format TaskbarFolders.sln --verify-no-changes
```

Biçim denetimi bir CI kapısıdır; göndermeden önce çalıştırın. Tam kurulum için [docs/developer-guide.md](docs/developer-guide.md) dosyasına bakın.

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

## Belgeler

* [Kullanıcı Kılavuzu](docs/user-guide.md)
* [Benutzerhandbuch](docs/benutzerhandbuch.md) (Almanca)
* [Sorun Giderme](docs/troubleshooting.md)
* [Mimari](docs/architecture.md)
* [Geliştirici Kılavuzu](docs/developer-guide.md)
* [API Referansı](docs/api-reference.md)
* [Sürüm Süreci](docs/release-process.md)
* [Mimari Karar Kayıtları](docs/adr/README.md)

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

## Katkıda Bulunma

Dal modeli, commit kuralları ve zorunlu kontroller için [CONTRIBUTING.md](CONTRIBUTING.md) dosyasına bakın. Hata raporları ve özellik istekleri [issue şablonları](.github/ISSUE_TEMPLATE) üzerinden yapılır. Soru sormak için [SUPPORT.md](SUPPORT.md), güvenlik açığı bildirimi için [SECURITY.md](SECURITY.md) dosyasını inceleyin.

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

## Lisans

MIT. Ayrıntılar için [LICENSE](LICENSE). Üçüncü taraf bileşenler [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) dosyasında listelenmiştir.

<p align="right">(<a href="#readme-top">başa dön</a>)</p>

<!-- MARKDOWN LINKS & IMAGES -->
[contributors-shield]: https://img.shields.io/github/contributors/aybrkaknc/taskbar-grouping.svg?style=for-the-badge
[contributors-url]: https://github.com/aybrkaknc/taskbar-grouping/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/aybrkaknc/taskbar-grouping.svg?style=for-the-badge
[forks-url]: https://github.com/aybrkaknc/taskbar-grouping/network/members
[stars-shield]: https://img.shields.io/github/stars/aybrkaknc/taskbar-grouping.svg?style=for-the-badge
[stars-url]: https://github.com/aybrkaknc/taskbar-grouping/stargazers
[issues-shield]: https://img.shields.io/github/issues/aybrkaknc/taskbar-grouping.svg?style=for-the-badge
[issues-url]: https://github.com/aybrkaknc/taskbar-grouping/issues
