#define DISCORDPP_IMPLEMENTATION
#include "discordpp.h"

#include <atomic>
#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <thread>

namespace {
std::mutex client_mutex;
std::shared_ptr<discordpp::Client> client;
std::uint64_t current_application_id = 0;
std::atomic<bool> callbacks_running{false};
std::thread callbacks_thread;

std::string safe(const char* value) {
    return value == nullptr ? std::string{} : std::string{value};
}

void copy_to_buffer(const std::string& value, char* destination, int capacity) {
    if (destination == nullptr || capacity <= 0) {
        return;
    }

    const auto count = std::min(value.size(), static_cast<std::size_t>(capacity - 1));
    std::memcpy(destination, value.data(), count);
    destination[count] = '\0';
}

void stop_callbacks() {
    callbacks_running.store(false);
    if (callbacks_thread.joinable()) {
        callbacks_thread.join();
    }
}
}

extern "C" __attribute__((visibility("default"))) int drpc_initialize(const char* application_id) {
    if (application_id == nullptr) {
        return 1;
    }

    char* end = nullptr;
    const auto parsed_id = std::strtoull(application_id, &end, 10);
    if (parsed_id == 0 || end == application_id || *end != '\0') {
        return 2;
    }

    std::lock_guard lock(client_mutex);
    if (client && current_application_id == parsed_id && callbacks_running.load()) {
        return 0;
    }
    stop_callbacks();
    client = std::make_shared<discordpp::Client>();
    client->SetApplicationId(static_cast<std::uint64_t>(parsed_id));
    current_application_id = static_cast<std::uint64_t>(parsed_id);

    callbacks_running.store(true);
    callbacks_thread = std::thread([] {
        while (callbacks_running.load()) {
            discordpp::RunCallbacks();
            std::this_thread::sleep_for(std::chrono::milliseconds(25));
        }
    });
    return 0;
}

extern "C" __attribute__((visibility("default"))) int drpc_set_activity(
    const char* details,
    const char* state,
    std::int64_t start_timestamp,
    std::int64_t end_timestamp,
    const char* large_image,
    const char* large_text,
    const char* small_image,
    const char* small_text,
    const char* button_label,
    const char* button_url) {
    std::lock_guard lock(client_mutex);
    if (!client) {
        return 1;
    }

    discordpp::Activity activity;
    activity.SetName("Deezer");
    activity.SetType(discordpp::ActivityTypes::Listening);
    activity.SetDetails(safe(details));
    activity.SetState(safe(state));

    if (start_timestamp > 0 && end_timestamp > start_timestamp) {
        discordpp::ActivityTimestamps timestamps;
        timestamps.SetStart(start_timestamp);
        timestamps.SetEnd(end_timestamp);
        activity.SetTimestamps(timestamps);
    }

    if ((large_image != nullptr && *large_image != '\0') ||
        (small_image != nullptr && *small_image != '\0')) {
        discordpp::ActivityAssets assets;
        if (large_image != nullptr && *large_image != '\0') {
            assets.SetLargeImage(safe(large_image));
            assets.SetLargeText(safe(large_text));
        }
        if (small_image != nullptr && *small_image != '\0') {
            assets.SetSmallImage(safe(small_image));
            assets.SetSmallText(safe(small_text));
        }
        activity.SetAssets(assets);
    }

    if (button_label != nullptr && *button_label != '\0' && button_url != nullptr && *button_url != '\0') {
        discordpp::ActivityButton button;
        button.SetLabel(safe(button_label));
        button.SetUrl(safe(button_url));
        activity.AddButton(button);
    }

    struct UpdateResult {
        std::mutex mutex;
        std::condition_variable ready;
        bool completed = false;
        bool successful = false;
    };
    auto update = std::make_shared<UpdateResult>();
    client->UpdateRichPresence(std::move(activity), [update](const discordpp::ClientResult& result) {
        {
            std::lock_guard result_lock(update->mutex);
            update->successful = result.Successful();
            update->completed = true;
        }
        update->ready.notify_one();
    });

    std::unique_lock result_lock(update->mutex);
    if (!update->ready.wait_for(result_lock, std::chrono::seconds(3), [&update] { return update->completed; })) {
        return 3;
    }
    return update->successful ? 0 : 4;
}

extern "C" __attribute__((visibility("default"))) void drpc_clear_activity() {
    std::lock_guard lock(client_mutex);
    if (client) {
        client->ClearRichPresence();
    }
}

extern "C" __attribute__((visibility("default"))) int drpc_get_connected_user(
    const char* application_id,
    char* user_id,
    int user_id_capacity,
    char* display_name,
    int display_name_capacity,
    char* username,
    int username_capacity,
    char* avatar_url,
    int avatar_url_capacity) {
    if (application_id == nullptr) {
        return 1;
    }

    char* end = nullptr;
    const auto parsed_id = std::strtoull(application_id, &end, 10);
    if (parsed_id == 0 || end == application_id || *end != '\0') {
        return 2;
    }

    std::lock_guard lock(client_mutex);
    if (!client) {
        return 3;
    }

    struct ConnectedUserResult {
        std::mutex mutex;
        std::condition_variable ready;
        bool completed = false;
        bool successful = false;
        std::string user_id;
        std::string display_name;
        std::string username;
        std::string avatar_url;
    };
    auto connected_user = std::make_shared<ConnectedUserResult>();
    client->GetDiscordClientConnectedUser(
        static_cast<std::uint64_t>(parsed_id),
        [connected_user](const discordpp::ClientResult& result, std::optional<discordpp::UserHandle> user) {
            {
                std::lock_guard result_lock(connected_user->mutex);
                connected_user->successful = result.Successful() && user.has_value();
                if (connected_user->successful) {
                    connected_user->user_id = std::to_string(user->Id());
                    connected_user->display_name = user->DisplayName();
                    connected_user->username = user->Username();
                    connected_user->avatar_url = user->AvatarUrl(
                        discordpp::UserHandle::AvatarType::Png,
                        discordpp::UserHandle::AvatarType::Png);
                }
                connected_user->completed = true;
            }
            connected_user->ready.notify_one();
        });

    std::unique_lock result_lock(connected_user->mutex);
    if (!connected_user->ready.wait_for(
            result_lock,
            std::chrono::seconds(3),
            [&connected_user] { return connected_user->completed; })) {
        return 4;
    }
    if (!connected_user->successful) {
        return 5;
    }

    copy_to_buffer(connected_user->user_id, user_id, user_id_capacity);
    copy_to_buffer(connected_user->display_name, display_name, display_name_capacity);
    copy_to_buffer(connected_user->username, username, username_capacity);
    copy_to_buffer(connected_user->avatar_url, avatar_url, avatar_url_capacity);
    return 0;
}

extern "C" __attribute__((visibility("default"))) void drpc_shutdown() {
    std::lock_guard lock(client_mutex);
    stop_callbacks();
    client.reset();
    current_application_id = 0;
}
