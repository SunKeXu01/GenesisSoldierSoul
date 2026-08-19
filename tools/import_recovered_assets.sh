#!/bin/sh
set -eu

repository_root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
workspace_root=$(CDPATH= cd -- "$repository_root/.." && pwd)
unity_resources="$repository_root/client-restored/Assets/Resources/OriginalGame"
legacy_assets="$repository_root/client/Assets"
recovered_client="$workspace_root/原程序恢复/单机怀旧创世兵魂V1.2.1/UnityProject/ExportedProject/Assets"
genesis_sound="$workspace_root/_解压资源/sound/sound"
cf_message="$workspace_root/CF2.0/CrossFire/rez/Snd2/TheFates/MESSAGE"
cf_sound="$workspace_root/CF2.0/CrossFire/rez/Snd2"
weapon_archive="$workspace_root/_解压资源/创世兵魂武器/武器"
weapon_cache="$workspace_root/_解压资源/创世兵魂素材/创世兵魂"
flash_converted="$repository_root/recovery/special-formats/flash-atf-converted"

copy_asset() {
    source_path=$1
    destination_path=$2
    if [ ! -f "$source_path" ]; then
        printf '缺少源资源: %s\n' "$source_path" >&2
        exit 1
    fi
    mkdir -p "$(dirname -- "$destination_path")"
    cp "$source_path" "$destination_path"
}

# 旧 Unity client 中恢复版遗漏的 HUD 原图。
copy_asset "$legacy_assets/Texture2D/Machinegun64x64.png" \
    "$unity_resources/UI/rifle_icon.png"
copy_asset "$legacy_assets/Texture2D/Pistol64x64.png" \
    "$unity_resources/UI/pistol_icon.png"
copy_asset "$legacy_assets/Texture2D/⁭Knife64x64.png" \
    "$unity_resources/UI/knife_icon.png"
copy_asset \
    "$legacy_assets/Texture2D/正面视角反恐射击游戏素材-准星_1(zhunxing_1)_爱给网_aigei_com.png" \
    "$unity_resources/UI/crosshair.png"

# 创世兵魂原版缓存中的受击、人物和回合音效。
copy_asset "$genesis_sound/weapon.93689/others/hit/bullet/hit1.mp3" \
    "$unity_resources/Audio/Genesis/bullet_hit.mp3"
copy_asset "$genesis_sound/character.84847/human/male01/harmed.mp3" \
    "$unity_resources/Audio/Genesis/harmed.mp3"
copy_asset "$genesis_sound/character.84847/human/male01/die.mp3" \
    "$unity_resources/Audio/Genesis/die.mp3"
copy_asset "$genesis_sound/radio.66759/english/male/Stage_Start_Default.mp3" \
    "$unity_resources/Audio/Genesis/stage_start.mp3"
copy_asset "$genesis_sound/radio.66759/english/male/Round_End_Win.mp3" \
    "$unity_resources/Audio/Genesis/round_win.mp3"

# 原版 AUG A1 基础枪皮和独立武器音效。模型由同一缓存中的
# auga1xilie A3D2 数据通过 client-restored/tools/a3d2_to_glb.py 恢复。
copy_asset "$weapon_cache/auga1.27760.png" \
    "$repository_root/client-restored/Assets/RecoveredWeapons/AUGA1/AUGA1.png"
copy_asset "$genesis_sound/weapon.93689/rifle/auga1/deploy.mp3" \
    "$unity_resources/Audio/AUGA1/deploy.mp3"
copy_asset "$genesis_sound/weapon.93689/rifle/auga1/fire.mp3" \
    "$unity_resources/Audio/AUGA1/fire.mp3"
copy_asset "$genesis_sound/weapon.93689/rifle/auga1/reload.mp3" \
    "$unity_resources/Audio/AUGA1/reload.mp3"

# 冰钻 AK47 使用 ak47xilie 的原 FP/TP A3D2 几何和该皮肤 material.swf
# 中解码的 512px ATF 枪体贴图；不使用商城缩略图替代 UV 材质。
copy_asset \
    "$flash_converted/material.32654__07125be89c36/binary/tag-000006-code-87-id-1.png" \
    "$repository_root/client-restored/Assets/RecoveredWeapons/AK47Ice/AK47Ice.png"
copy_asset "$genesis_sound/weapon.93689/rifle/ak47/deploy.mp3" \
    "$unity_resources/Audio/AK47Ice/deploy.mp3"
copy_asset "$genesis_sound/weapon.93689/rifle/ak47/fire.mp3" \
    "$unity_resources/Audio/AK47Ice/fire.mp3"
copy_asset "$genesis_sound/weapon.93689/rifle/ak47/reload.mp3" \
    "$unity_resources/Audio/AK47Ice/reload.mp3"

# CF2.0 已直接解包的 PCM 播报；不运行包内来源不明的 Windows 程序。
copy_asset "$cf_message/Headshot_GR.wav" \
    "$unity_resources/Audio/CF2/headshot.wav"
copy_asset "$cf_message/MultiKill_2_GR.wav" \
    "$unity_resources/Audio/CF2/double_kill.wav"
copy_asset "$cf_message/MultiKill_3_GR.wav" \
    "$unity_resources/Audio/CF2/triple_kill.wav"
copy_asset "$cf_message/MultiKill_4_GR.wav" \
    "$unity_resources/Audio/CF2/multi_kill.wav"
copy_asset "$cf_message/knifekill_GR.wav" \
    "$unity_resources/Audio/CF2/knife_kill.wav"
copy_asset "$cf_message/Stage_Start_TDM_GR.wav" \
    "$unity_resources/Audio/CF2/stage_start.wav"
copy_asset "$cf_message/Round_End_Win_GR.wav" \
    "$unity_resources/Audio/CF2/round_win.wav"
copy_asset "$cf_message/Fireinthehole_Grenade_GR.wav" \
    "$unity_resources/Audio/CF2/Grenade/fire_in_the_hole.wav"
copy_asset "$cf_sound/Submarine/Submarine_Grenade_Boom.wav" \
    "$unity_resources/Audio/CF2/Grenade/explosion.wav"

# Shotgun01 自身恢复工程中的开火/泵动声，以及同一原版声音池中的
# M1887 装备和装填声。避免霰弹枪继续错误复用 M4A1 音效。
copy_asset "$recovered_client/AudioClip/ShotgunFirePump.ogg" \
    "$unity_resources/Audio/Shotgun01/fire.ogg"
copy_asset "$genesis_sound/weapon.93689/shotgun/m1887/deploy.mp3" \
    "$unity_resources/Audio/Shotgun01/deploy.mp3"
copy_asset "$genesis_sound/weapon.93689/shotgun/m1887/reload.mp3" \
    "$unity_resources/Audio/Shotgun01/reload.mp3"

# 原始第一人称步枪包含双手骨骼以及开火、换弹、待机和切枪动画。
first_person="$unity_resources/FirstPerson"
copy_asset "$legacy_assets/GameObject/AssaultRifle01.prefab" \
    "$first_person/AssaultRifle01.prefab"
copy_asset "$legacy_assets/GameObject/AssaultRifle01.prefab.meta" \
    "$first_person/AssaultRifle01.prefab.meta"

for asset_name in Fire Reload ReloadEmpty Idle01_0 Idle02_0 Idle03_0 Idle04_0 Wield
do
    copy_asset "$legacy_assets/AnimationClip/$asset_name.anim" \
        "$first_person/Animations/$asset_name.anim"
    copy_asset "$legacy_assets/AnimationClip/$asset_name.anim.meta" \
        "$first_person/Animations/$asset_name.anim.meta"
done

for asset_name in AssaultRifle01 MA_Arm
do
    copy_asset "$legacy_assets/Material/$asset_name.mat" \
        "$first_person/Materials/$asset_name.mat"
    copy_asset "$legacy_assets/Material/$asset_name.mat.meta" \
        "$first_person/Materials/$asset_name.mat.meta"
done

for asset_name in Clip_0 Mantel Trigger Main Left Right
do
    copy_asset "$legacy_assets/Mesh/$asset_name.asset" \
        "$first_person/Meshes/$asset_name.asset"
    copy_asset "$legacy_assets/Mesh/$asset_name.asset.meta" \
        "$first_person/Meshes/$asset_name.asset.meta"
done

for asset_name in N_AssaultRifle01 NM_Arms01 DI_Arms01 D_AssaultRifle01
do
    copy_asset "$legacy_assets/Texture2D/$asset_name.png" \
        "$first_person/Textures/$asset_name.png"
    copy_asset "$legacy_assets/Texture2D/$asset_name.png.meta" \
        "$first_person/Textures/$asset_name.png.meta"
done

# 同一套第一人称骨骼的完整手枪，替换残缺世界模型造成的尖刺袖子。
copy_asset "$legacy_assets/GameObject/Pistol01.prefab" \
    "$first_person/Pistol/Pistol01.prefab"
copy_asset "$legacy_assets/GameObject/Pistol01.prefab.meta" \
    "$first_person/Pistol/Pistol01.prefab.meta"

for asset_name in Idle01_2 Idle02_2 Idle03_2 Idle04_2 Fire_0 FireEmpty \
    ReloadEmpty_0 StandardReload "Out of Ammo"
do
    copy_asset "$legacy_assets/AnimationClip/$asset_name.anim" \
        "$first_person/Animations/$asset_name.anim"
    copy_asset "$legacy_assets/AnimationClip/$asset_name.anim.meta" \
        "$first_person/Animations/$asset_name.anim.meta"
done

copy_asset "$legacy_assets/Material/MA_Pistol01.mat" \
    "$first_person/Materials/MA_Pistol01.mat"
copy_asset "$legacy_assets/Material/MA_Pistol01.mat.meta" \
    "$first_person/Materials/MA_Pistol01.mat.meta"

for asset_name in HammerMesh ClipSpawnMesh TriggerMesh MainMesh \
    LeftSwitchMesh Right_2 ReloadMesh ClipMesh Left_1 TopPartMesh \
    RightSwitchMesh FireMesh
do
    copy_asset "$legacy_assets/Mesh/$asset_name.asset" \
        "$first_person/Meshes/$asset_name.asset"
    copy_asset "$legacy_assets/Mesh/$asset_name.asset.meta" \
        "$first_person/Meshes/$asset_name.asset.meta"
done

for asset_name in NM_Pistol01 DI_Pistol01
do
    copy_asset "$legacy_assets/Texture2D/$asset_name.png" \
        "$first_person/Textures/$asset_name.png"
    copy_asset "$legacy_assets/Texture2D/$asset_name.png.meta" \
        "$first_person/Textures/$asset_name.png.meta"
done

# 旧客户端完整近战手臂与投掷/挥砍动画，替换二维刀具遮罩。
copy_asset "$legacy_assets/GameObject/Knife01.prefab" \
    "$first_person/Knife/Knife01.prefab"
copy_asset "$legacy_assets/GameObject/Knife01.prefab.meta" \
    "$first_person/Knife/Knife01.prefab.meta"

for asset_name in Idle01_1 Idle02_1 Idle03_1 Idle04_1 Throw
do
    copy_asset "$legacy_assets/AnimationClip/$asset_name.anim" \
        "$first_person/Animations/$asset_name.anim"
    copy_asset "$legacy_assets/AnimationClip/$asset_name.anim.meta" \
        "$first_person/Animations/$asset_name.anim.meta"
done

copy_asset "$legacy_assets/Material/Knife01.mat" \
    "$first_person/Materials/Knife01.mat"
copy_asset "$legacy_assets/Material/Knife01.mat.meta" \
    "$first_person/Materials/Knife01.mat.meta"

for asset_name in Knife Right_0
do
    copy_asset "$legacy_assets/Mesh/$asset_name.asset" \
        "$first_person/Meshes/$asset_name.asset"
    copy_asset "$legacy_assets/Mesh/$asset_name.asset.meta" \
        "$first_person/Meshes/$asset_name.asset.meta"
done

for asset_name in N_Knife01 D_Knife01
do
    copy_asset "$legacy_assets/Texture2D/$asset_name.png" \
        "$first_person/Textures/$asset_name.png"
    copy_asset "$legacy_assets/Texture2D/$asset_name.png.meta" \
        "$first_person/Textures/$asset_name.png.meta"
done

# 恢复工程中的第三人称人形动画，与 RemotePlayer 的 Marine 骨架共用
# Humanoid Avatar。用于替换代码直接摆动四肢造成的僵硬走路。
character_animation="$unity_resources/Character/Animations"
for asset_name in StandIdleOneHand WalkForward Run Jump MotionJump \
    WalkBackwardOneHand WalkStrafeLeftOneHand WalkStrafeRightOneHand \
    RunBackwardOneHand RunStrafeLeftOneHand RunStrafeRightOneHand
do
    copy_asset "$recovered_client/AnimationClip/$asset_name.anim" \
        "$character_animation/$asset_name.anim"
    copy_asset "$recovered_client/AnimationClip/$asset_name.anim.meta" \
        "$character_animation/$asset_name.anim.meta"
done

# 根目录武器包中的可直接由 Unity 导入的模型。原文件保留在隔离解压
# 目录；这里仅同步运行时会引用的 FBX/OBJ 和可用贴图。
copy_asset "$weapon_archive/AK74m/AK-74M.FBX" \
    "$unity_resources/Weapons/AK74M/AK-74M.FBX"
copy_asset "$weapon_archive/AWP/awp.obj" \
    "$unity_resources/Weapons/AWP/awp.obj"
copy_asset "$weapon_archive/M9手枪/M9.obj" \
    "$unity_resources/Weapons/M9/M9.obj"
# M9.png is a rendered showcase image, not a UV texture. The lowercase JPG is
# the 2048x2048 diffuse atlas referenced by the OBJ's Tex_0009 material.
copy_asset "$weapon_archive/M9手枪/m9.jpg" \
    "$unity_resources/Weapons/M9/m9.jpg"
copy_asset "$weapon_archive/M9手枪/M9.png" \
    "$unity_resources/Weapons/M9/M9_reference.png"

printf '已同步恢复资源到: %s\n' "$unity_resources"
