# 💎 Patch scp-939 for **SCP:SL RP-server**.
📦 Features:

1. Added a custom Sound Trap for SCP-939 (hardcoded medkit model)).
* They are in the `HCZ Armory` by default (2 items).
---
2. Holding "C" (default) and jumping at a door as SCP-939 gives a chance to smash it.
* ⚙️ If you want to change the chance, modify "30" to another number in the DoorCrash.cs
```csharp
if (UnityEngine.Random.Range(0, 100) >= 30) return;
``` 
---
3. Allows SCP-939 to deploy the amnesiac cloud anywhere via `.gas` console command.
---
4. Disables broken and immersion-breaking blips (heartbeat and POI markers)
---
5. Chaos and MTF are **immune** to the amnesiac cloud.
---

# 🛠️ Not all files in the archive. 

Add files in /references:

* 0Harmony.dll
* Assembly-CSharp.dll
* CommandSystem.Core.dll
* Assembly-CSharp-firstpass.dll
* Exiled.API.dll
* Exiled.Events.dll
* Exiled.CustomItems.dll
* Exiled.Loader.dll
* LabApi.dll
* Mirror.dll
* NorthwoodLib.dll
* Pooling.dll
* UnityEngine.CoreModule.dll
* UnityEngine.PhysicsModule.dll
* UnityEngine.JSONSerializeModule.dll
---
# 🌟 credits
* Special thanks to **denissunstrike** for providing the sound trap model. [Discord]
* Plugin has been created for RP-servers,
* Version Exilied: v9.14.2
* Discord: SairwX04410


