# DeezerRPC

DeezerRPC publie le morceau actuellement joué sur Deezer dans la Rich Presence Discord, avec la même règle visuelle sur Android et Windows : **pochette seule, titre, artiste, album, progression native et bouton Deezer**.

## État du projet

| Plateforme | Détection Deezer | Rich Presence | Livrable |
|---|---|---|---|
| Windows 10/11 | Application Deezer via GSMTC ; navigateurs en option | Fonctionnelle via le compte ouvert dans Discord Desktop | [Télécharger l’EXE Windows](https://github.com/TomizeCorp/DeezerRPC/releases/latest) |
| Android 7+ | Session média Deezer via `NotificationListenerService` | Fonctionnelle via Discord Social SDK 1.10.18687 | [Télécharger l’APK Android complet](https://github.com/TomizeCorp/DeezerRPC/releases/latest) |

Les fichiers prêts à installer sont disponibles dans la [dernière version GitHub](https://github.com/TomizeCorp/DeezerRPC/releases/latest). Les dossiers `artifacts/` restent volontairement exclus de l’historique Git pour éviter d’alourdir le dépôt.

Le SDK Social Discord est téléchargé depuis le portail de l’application Discord et son archive n’est pas redistribuée dans ce dépôt. L’APK officiel publié inclut toutefois les bibliothèques d’exécution Android autorisées afin que la Rich Presence fonctionne sans méthode non officielle ni jeton utilisateur.

## Rich Presence

Le mapping partagé est volontairement strict :

- `details` : nom du morceau ;
- `state` : artiste uniquement ; l’album n’est jamais répété à côté de l’artiste ;
- `assets.large_image` : URL HTTPS de la pochette Deezer ;
- aucun champ `small_image` ou `small_text` n’existe dans le modèle ;
- `timestamps.start/end` : progression rendue nativement par Discord pendant la lecture ;
- barre pleine largeur `Écouter sur Deezer` sous le morceau : lien direct lorsqu’il est résolu, recherche titre/artiste sur Deezer en secours dans l’interface ;
- l’application affiche le titre, puis l’artiste, puis l’album sur des lignes distinctes ; la ligne album disparaît lorsqu’elle n’est pas disponible.

Sur Android, les logos Discord et Deezer sont dessinés en monochrome et reprennent automatiquement la couleur du texte voisin. L’accès Discord se trouve à droite de `Paramètres` dans la navigation inférieure : une fois le compte détecté, sa photo ouvre un aperçu du profil et l’action `Se déconnecter`. L’ancienne carte d’état et l’ancien bouton `Connecter Discord` ont été retirés de l’accueil.

La résolution de pochette vérifie l’album fourni par Deezer, accepte ses variantes légitimes (deluxe, remaster, collaborateurs) et relance une recherche élargie lorsque la recherche stricte ne renvoie rien. Elle gère également les résultats où Deezer ne renvoie que l’artiste principal — par exemple `David Guetta` pour un morceau crédité `David Guetta & Bebe Rexha` dans le lecteur.

Discord n’offre que deux lignes de texte personnalisables en plus du nom de l’application. L’artiste et l’album partagent donc la deuxième ligne. Les boutons ne sont visibles que par les autres utilisateurs qui consultent la présence, conformément au comportement Discord.

## Utilisation Windows

1. Lancer `DeezerRPC.exe` : le nouveau tableau de bord affiche la lecture, la connexion Discord et l’état de Deezer.
2. Cliquer sur la carte Discord si Discord Desktop n’est pas encore ouvert, puis se connecter normalement dans Discord.
3. Lire un morceau dans Deezer. La fenêtre peut ensuite être fermée : l’application reste dans la zone de notification.

L’Application ID `1540336569532031116` est intégré à la compilation : aucun identifiant ou réglage Discord n’est demandé à l’utilisateur.

Sur Windows, « connecté » signifie que Deezer Presence utilise automatiquement le compte déjà authentifié dans Discord Desktop. L’application ne demande et ne conserve donc jamais le mot de passe, le secret client ou un token utilisateur Discord.

L’application peut s’enregistrer dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` pour démarrer avec Windows. Elle interroge la session média locale une fois par seconde, puis laisse Discord faire avancer le temps sans republier l’activité en boucle. Lors d’une pause, l’activité Discord est entièrement supprimée et elle revient automatiquement à la reprise. Les réglages permettent aussi de masquer l’album, la progression ou le bouton sans jamais ajouter de petite image.

La détection Web est désactivée par défaut. Une session média de navigateur ne révèle pas son URL à GSMTC ; lorsqu’elle est activée, DeezerRPC ne retient la session que si titre/artiste/album correspondent au catalogue Deezer. C’est utile pour Deezer Web, mais moins certain que l’application Deezer dédiée.

## Activation Discord sur Android

Discord Social SDK **1.10 ou plus récent** prend en charge Android 7+ et la publication sans OAuth vers l’utilisateur connecté dans Discord Android. Pour produire l’APK complet :

1. Activer Discord Social SDK pour l’application dans le portail Discord.
2. Télécharger l’archive **Standalone C++** Android 1.10+ et l’extraire dans `vendor/discord-social-sdk`.
3. Installer Android NDK et CMake.
4. Construire le pont natif :

```powershell
./scripts/build-android-native.ps1 `
  -DiscordSdkDirectory ./vendor/discord-social-sdk `
  -AndroidNdkDirectory C:/Android/Sdk/ndk/<version> `
  -CMakePath C:/Android/Sdk/cmake/<version>/bin/cmake.exe
```

5. Construire l’APK avec `./scripts/build-android.ps1`.
6. Dans l’application, autoriser l’accès média, toucher le logo Discord à droite de `Paramètres`, puis lancer un morceau dans Deezer. La connexion et la publication sont automatiques ; l’Application ID est déjà intégré.

Le manifeste déclare explicitement `com.discord`, requis par Android 11+, et le pont natif utilise le compte déjà ouvert dans Discord : pas de serveur, OAuth ou jeton utilisateur.

Le mode arrière-plan Android utilise une notification permanente : elle est nécessaire pour améliorer la continuité de la détection lorsque l’écran est éteint. L’activité est republiée à intervalle contrôlé et le pont Discord est recréé après une interruption, indépendamment de la connexion à un salon vocal. Android peut malgré tout arrêter une application selon les règles d’économie d’énergie du constructeur.

## Fonctionnement « H24 »

Deezer Presence n’a besoin d’aucun hébergement. Elle peut rester active au démarrage, en zone de notification sur Windows et via le service média Android. Une Rich Presence ne peut cependant pas rester en ligne si l’appareil est éteint, si Deezer Presence est arrêté de force ou si aucun client Discord compatible n’est joignable. Une présence réellement indépendante des appareils exigerait un serveur, ce que ce projet évite volontairement.

## Compilation

Prérequis Windows : SDK .NET 8.

```powershell
dotnet run --project tests/DeezerRpc.Core.Tests -c Release
./scripts/build-windows.ps1
```

Pour compiler l’APK de détection sans le binaire Discord propriétaire :

```powershell
./scripts/build-android.ps1 -DetectorOnly
```

## Architecture

- `src/DeezerRpc.Core` : modèle de morceau, résolution catalogue Deezer et construction Rich Presence partagés ;
- `src/DeezerRpc.Windows` : GSMTC, client RPC local Discord, zone de notification et démarrage Windows ;
- `src/DeezerRpc.Android` : `MediaSessionManager`, service en arrière-plan et pont Discord Social SDK ;
- `tests/DeezerRpc.Core.Tests` : contrats du design, dont l’absence obligatoire de petite image.

Références techniques : [Rich Presence Discord](https://docs.discord.com/developers/discord-social-sdk/development-guides/setting-rich-presence), [compatibilité Discord Social SDK](https://docs.discord.com/developers/discord-social-sdk/core-concepts/platform-compatibility), [GSMTC Windows](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmanager), [MediaSessionManager Android](https://developer.android.com/reference/android/media/session/MediaSessionManager).
