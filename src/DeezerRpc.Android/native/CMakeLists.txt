cmake_minimum_required(VERSION 3.22)
project(DeezerRpcDiscordBridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if(NOT DEFINED DISCORD_SDK_ROOT)
    message(FATAL_ERROR "DISCORD_SDK_ROOT must point to the extracted Discord Social SDK 1.10+ C++ archive")
endif()

set(DISCORD_INCLUDE_DIR "${DISCORD_SDK_ROOT}/include")
find_library(
    DISCORD_PARTNER_SDK
    NAMES discord_partner_sdk libdiscord_partner_sdk
    PATHS
        "${DISCORD_SDK_ROOT}/lib/release/${ANDROID_ABI}"
        "${DISCORD_SDK_ROOT}/lib/${ANDROID_ABI}/release"
        "${DISCORD_SDK_ROOT}/lib/${ANDROID_ABI}"
        "${DISCORD_SDK_ROOT}/bin/release/${ANDROID_ABI}"
    NO_DEFAULT_PATH)

if(NOT DISCORD_PARTNER_SDK)
    message(FATAL_ERROR "libdiscord_partner_sdk.so was not found for ABI ${ANDROID_ABI}")
endif()

add_library(deezerrpc_discord_bridge SHARED discord_bridge.cpp)
target_include_directories(deezerrpc_discord_bridge PRIVATE "${DISCORD_INCLUDE_DIR}")
target_link_libraries(deezerrpc_discord_bridge PRIVATE "${DISCORD_PARTNER_SDK}" log android)

