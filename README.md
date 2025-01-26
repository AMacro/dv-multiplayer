﻿﻿[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![Apache 2.0 License][license-shield]][license-url]


<!-- PROJECT LOGO -->
<div align="center">
  <h1>Derail Valley Multiplayer</h1>
  <p>
    A <a href="https://store.steampowered.com/app/588030">Derail Valley</a> mod that adds multiplayer.
    <br />
    <br />
    <a href="https://github.com/AMacro/dv-multiplayer/issues">Report Bug</a>
    ·
    <a href="https://github.com/AMacro/dv-multiplayer/issues">Request Feature</a>
    ·
    <a href="https://discord.com/channels/332511223536943105/1234574186161377363" target="blank" rel="noopener noreferrer">Discord</a>
  </p>
</div>




<!-- TABLE OF CONTENTS -->
<details>
  <summary>Table of Contents</summary>
  <ol>
    <li><a href="#about-the-project">About The Project</a></li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#building">Building</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#translations">Translations</a></li>
    <li><a href="#license">License</a></li>
  </ol>
</details>

# Important!
At present, assume all other mods are incompatible!
Some mods may work, but many do cause issues and break multiplayer capabilities.

Our primary focus is to have the vanilla game working in multiplayer; once this is achieved we will then work on compatibility with other mods.


<!-- ABOUT THE PROJECT -->
## About The Project

Multiplayer is a Derail Valley mod that adds multiplayer to the game, allowing you to play with your friends.

It works by having one player host a game, and then other players can join that game.

This fork is a continuation of [Insprill's](https://github.com/Insprill/dv-multiplayer) amazing efforts.



<!-- Roadmap -->
## Roadmap

For a list of planned features, see the [project board][project-board-url].  
The mod will be released on Nexus once it's ready.




<!-- BUILDING -->
## Building

Before building, make sure you have <a href="https://dotnet.microsoft.com/en-us/download">.NET 7</a>, <a href="https://github.com/PowerShell/PowerShell#get-powershell">PowerShell 7</a>, <a href="https://visualstudio.microsoft.com/">Visual Studio</a>, and <a href="https://unity.com/releases/editor/archive">Unity 2019.4.40f1</a> installed. You'll also need the <a href="https://unity.com/download">Unity Hub</a> in order to open Unity.

<ol>
  <li>Clone the repository to a memorable location.</li>
  <li>Open the Unity Hub, click <code>Open</code>, then select the <code>MultiplayerAssets</code> folder. This should open Unity <strong>2019.4.40f1</strong>. Make sure that it is this version <em>exactly</em>, otherwise things will break.</li>
  <li>In Unity, go to the top and click <code>Multiplayer > Build Assets and Scripts</code>.</li>
  <li>
    Create a file called <code>Directory.Build.Targets</code> in the root of your extracted zip, open it and paste the following:

    <Project>
        <PropertyGroup>
            <DvInstallDir>C:\Program Files (x86)\Steam\steamapps\common\Derail Valley</DvInstallDir>
            <UnityInstallDir>C:\Program Files\Unity\Hub\Editor\2019.4.40f1\Editor</UnityInstallDir>
            <ReferencePath>
                $(DvInstallDir)\DerailValley_Data\Managed\;
                $(DvInstallDir)\DerailValley_Data\Managed\UnityModManager\;
                $(UnityInstallDir)\Data\Managed\
            </ReferencePath>
            <AssemblySearchPaths>$(AssemblySearchPaths);$(ReferencePath);</AssemblySearchPaths>
        </PropertyGroup>
    </Project>

  This will specify where the references that are needed to build the mod are located. <strong>IMPORTANT!</strong> Make sure that the file paths <code>DvInstallDir</code> and <code>UnityInstallDir</code> are the same as wherever you have your installs. 
  </li>
  <li>
    Open the <code>Multiplayer.sln</code> file in Visual Studio, then go to <code>Build > Build Solution</code>. This will build the dll file into the build folder. It should also automatically package the mod into your DV mod folder, so you should now be able to open Derail Valley.</li> 
  </li>
</ol>

<!-- CONTRIBUTING -->
## Contributing

Contributions are what make the open source community such an amazing place to learn, inspire, and create.  
Any contributions you make are **greatly appreciated**!  
If you're new to contributing to open-source projects, you can follow [this][contributing-quickstart-url] guide.




<!-- TRANSLATIONS -->

## Translations

Special thanks to those who have assisted with translations - Apologies if I've missed you, drop me a line and I'll update this section.
If you'd like to help with translations, please create a pull request or send a message on our [Discord channel](https://discord.com/channels/332511223536943105/1234574186161377363).
| **Translator** | **Language** |
| :------------ | :------------ 
| Ádi | Hungarian   |
| My Name Is BorING | Chinese (Simplified) |
| Harfeur | French |




<!-- LICENSE -->
## License

Code is distributed under the Apache 2.0 license.  
See [LICENSE][license-url] for more information.


<!-- MARKDOWN LINKS & IMAGES -->
<!-- https://www.markdownguide.org/basic-syntax/#reference-style-links -->

[contributors-shield]: https://img.shields.io/github/contributors/AMacro/dv-multiplayer.svg?style=for-the-badge
[contributors-url]: https://github.com/AMacro/dv-multiplayer/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/AMacro/dv-multiplayer.svg?style=for-the-badge
[forks-url]: https://github.com/AMacro/dv-multiplayer/network/members
[stars-shield]: https://img.shields.io/github/stars/AMacro/dv-multiplayer.svg?style=for-the-badge
[stars-url]: https://github.com/AMacro/dv-multiplayer/stargazers
[issues-shield]: https://img.shields.io/github/issues/AMacro/dv-multiplayer.svg?style=for-the-badge
[issues-url]: https://github.com/AMacro/dv-multiplayer/issues
[license-shield]: https://img.shields.io/github/license/AMacro/dv-multiplayer.svg?style=for-the-badge
[license-url]: https://github.com/AMacro/dv-multiplayer/blob/master/LICENSE
[altfuture-support-email-url]: mailto:support@altfuture.gg
[contributing-quickstart-url]: https://docs.github.com/en/get-started/quickstart/contributing-to-projects
[asset-studio-url]: https://github.com/Perfare/AssetStudio
[mapify-building-docs]: https://dv-mapify.readthedocs.io/en/latest/contributing/building/
[project-board-url]: https://github.com/users/AMacro/projects/2
