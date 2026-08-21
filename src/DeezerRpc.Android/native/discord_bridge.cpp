#define DISCORDPP_IMPLEMENTATION
#include "discordpp.h"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstdlib>
#include <memory>
#include <mutex>
#include <string>
#include <thread>

namespace {
std::mutex client_mutex;
std::shared_ptr<discordpp::Client> client;
std::atomic<bool> callbacks_running{false};
std::thread callbacks_thread;

std::string safe(const char* value) {
    return value == nullptr ? std::string{} : std::string{value};
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
    stop_callbacks();
    client = std::make_shared<discordpp::Client>();
    client->SetApplicationId(static_cast<std::uint64_t>(parsed_id));

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

    if (large_image != nullptr && *large_image != '\0') {
        discordpp::ActivityAssets assets;
        assets.SetLargeImage(safe(large_image));
        assets.SetLargeText(safe(large_text));
        // Deliberately no SetSmallImage or SetSmallText call.
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

extern "C" __attribute__((visibility("default"))) void drpc_shutdown() {
    std::lock_guard lock(client_mutex);
    stop_callbacks();
    client.reset();
}
