# Configuration Discord

## Application

L’Application ID `1540336569532031116` est intégré à DeezerRPC. L’utilisateur final n’a aucun identifiant à saisir. DeezerRPC ne demande jamais le secret client, un token de bot ou le token du compte utilisateur.

Sur Windows, le bouton de connexion ouvre Discord Desktop et Deezer Presence détecte automatiquement le compte déjà connecté par le canal RPC local. Il n’existe donc aucun écran d’Application ID ou de token dans l’application.

Donnez à l’application un nom court, par exemple `Deezer`, car Discord affiche automatiquement ce nom au-dessus des deux lignes personnalisées.

## Android

Dans la rubrique Discord Social SDK de l’application :

1. activez l’intégration ;
2. téléchargez la version Standalone C++ 1.10+ contenant Android ;
3. conservez la structure `include/discordpp.h` et les bibliothèques `libdiscord_partner_sdk.so` par ABI ;
4. exécutez `scripts/build-android-native.ps1`.

La version 1.10 est nécessaire : elle permet au SDK de publier une présence vers le client Discord Android connecté. L’archive propriétaire doit être ajoutée avant de produire l’APK complet ; l’APK « detector-only » ne prétend pas être connecté quand le SDK est absent.

## Vérification visuelle

Utilisez un second compte Discord pour vérifier le bouton, car Discord ne montre pas les boutons personnalisés au propriétaire de la présence.

Contrôlez les invariants suivants :

- une seule image, la pochette carrée ;
- aucune icône accolée, même en pause ;
- titre sur la première ligne personnalisée ;
- artiste seul sur la seconde ligne Discord ; album séparé dans l’interface de Deezer Presence ;
- progression Discord pendant la lecture, aucun chronomètre actif en pause ;
- bouton ouvrant `https://www.deezer.com/track/...`.
