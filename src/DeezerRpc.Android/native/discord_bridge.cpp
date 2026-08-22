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
std::mutex auth_mutex;
std::shared_ptr<discordpp::Client> client;
std::uint64_t current_application_id = 0;
std::atomic<bool> callbacks_running{false};
std::thread callbacks_thread;

struct AuthorizationResult {
    std::mutex mutex;
    std::condition_variable ready;
    bool completed = false;
    bool successful = false;
    std::string code;
    std::string redirect_uri;
    std::string code_verifier;
};

std::shared_ptr<AuthorizationResult> pending_authorization;

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

bool parse_application_id(const char* application_id, std::uint64_t& parsed_id) {
    if (application_id == nullptr) {
        return false;
    }

    char* end = nullptr;
    parsed_id = std::strtoull(application_id, &end, 10);
    return parsed_id != 0 && end != application_id && *end == '\0';
}

std::shared_ptr<discordpp::Client> get_client() {
    std::lock_guard lock(client_mutex);
    return client;
}

int update_token_and_connect(
    const std::shared_ptr<discordpp::Client>& target,
    const std::string& access_token) {
    if (!target || access_token.empty()) {
        return 1;
    }

    if (target->GetStatus() == discordpp::Client::Status::Ready && target->IsAuthenticated()) {
        return 0;
    }

    struct TokenUpdateResult {
        std::mutex mutex;
        std::condition_variable ready;
        bool completed = false;
        bool successful = false;
    };
    auto token_update = std::make_shared<TokenUpdateResult>();
    target->UpdateToken(
        discordpp::AuthorizationTokenType::Bearer,
        access_token,
        [token_update](const discordpp::ClientResult& result) {
            {
                std::lock_guard result_lock(token_update->mutex);
                token_update->successful = result.Successful();
                token_update->completed = true;
            }
            token_update->ready.notify_one();
        });

    std::unique_lock token_lock(token_update->mutex);
    if (!token_update->ready.wait_for(
            token_lock,
            std::chrono::seconds(10),
            [&token_update] { return token_update->completed; })) {
        return 2;
    }
    if (!token_update->successful) {
        return 3;
    }
    token_lock.unlock();

    target->Connect();
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(20);
    while (std::chrono::steady_clock::now() < deadline) {
        const auto status = target->GetStatus();
        if (status == discordpp::Client::Status::Ready) {
            return 0;
        }
        if (status == discordpp::Client::Status::Disconnected &&
            std::chrono::steady_clock::now() + std::chrono::seconds(2) < deadline) {
            target->Connect();
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }
    return 4;
}

int exchange_authorization_code(
    const std::shared_ptr<discordpp::Client>& target,
    std::uint64_t application_id,
    const std::string& code,
    const std::string& code_verifier,
    const std::string& redirect_uri,
    char* access_token,
    int access_token_capacity,
    char* refresh_token,
    int refresh_token_capacity,
    std::int64_t* expires_in_seconds) {
    if (!target || code.empty() || code_verifier.empty() || redirect_uri.empty()) {
        return 1;
    }

    struct TokenExchangeResult {
        std::mutex mutex;
        std::condition_variable ready;
        bool completed = false;
        bool successful = false;
        std::string access_token;
        std::string refresh_token;
        std::int64_t expires_in = 0;
    };
    auto exchange = std::make_shared<TokenExchangeResult>();
    target->GetToken(
        application_id,
        code,
        code_verifier,
        redirect_uri,
        [exchange](
            const discordpp::ClientResult& result,
            std::string new_access_token,
            std::string new_refresh_token,
            discordpp::AuthorizationTokenType,
            std::int32_t expires_in,
            std::string) {
            {
                std::lock_guard result_lock(exchange->mutex);
                exchange->successful = result.Successful();
                exchange->access_token = std::move(new_access_token);
                exchange->refresh_token = std::move(new_refresh_token);
                exchange->expires_in = expires_in;
                exchange->completed = true;
            }
            exchange->ready.notify_one();
        });

    std::unique_lock exchange_lock(exchange->mutex);
    if (!exchange->ready.wait_for(
            exchange_lock,
            std::chrono::seconds(30),
            [&exchange] { return exchange->completed; })) {
        return 5;
    }
    if (!exchange->successful || exchange->access_token.empty() || exchange->refresh_token.empty()) {
        return 6;
    }

    copy_to_buffer(exchange->access_token, access_token, access_token_capacity);
    copy_to_buffer(exchange->refresh_token, refresh_token, refresh_token_capacity);
    if (expires_in_seconds != nullptr) {
        *expires_in_seconds = exchange->expires_in;
    }
    return 0;
}
}

extern "C" __attribute__((visibility("default"))) int drpc_initialize(const char* application_id) {
    std::uint64_t parsed_id = 0;
    if (!parse_application_id(application_id, parsed_id)) {
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

extern "C" __attribute__((visibility("default"))) int drpc_begin_authorize(
    const char* application_id) {
    std::uint64_t parsed_id = 0;
    if (!parse_application_id(application_id, parsed_id)) {
        return 1;
    }
    if (drpc_initialize(application_id) != 0) {
        return 2;
    }

    auto target = get_client();
    if (!target) {
        return 2;
    }

    auto verifier = target->CreateAuthorizationCodeVerifier();
    discordpp::AuthorizationArgs args;
    args.SetClientId(parsed_id);
    args.SetScopes(discordpp::Client::GetDefaultPresenceScopes());
    args.SetCodeChallenge(verifier.Challenge());

    auto authorization = std::make_shared<AuthorizationResult>();
    authorization->code_verifier = verifier.Verifier();
    {
        std::lock_guard auth_lock(auth_mutex);
        pending_authorization = authorization;
    }

    // On Android Authorize ultimately calls Activity.startActivity. This entry point is
    // deliberately non-blocking so managed code can invoke it from the UI thread.
    target->Authorize(
        std::move(args),
        [authorization](
            const discordpp::ClientResult& result,
            std::string code,
            std::string redirect_uri) {
            {
                std::lock_guard result_lock(authorization->mutex);
                authorization->successful = result.Successful();
                authorization->code = std::move(code);
                authorization->redirect_uri = std::move(redirect_uri);
                authorization->completed = true;
            }
            authorization->ready.notify_one();
        });
    return 0;
}

extern "C" __attribute__((visibility("default"))) int drpc_finish_authorize(
    const char* application_id,
    char* access_token,
    int access_token_capacity,
    char* refresh_token,
    int refresh_token_capacity,
    std::int64_t* expires_in_seconds) {
    std::uint64_t parsed_id = 0;
    if (!parse_application_id(application_id, parsed_id)) {
        return 1;
    }

    auto target = get_client();
    if (!target) {
        return 2;
    }

    std::shared_ptr<AuthorizationResult> authorization;
    {
        std::lock_guard auth_lock(auth_mutex);
        authorization = pending_authorization;
    }
    if (!authorization) {
        return 2;
    }

    std::unique_lock authorization_lock(authorization->mutex);
    if (!authorization->ready.wait_for(
            authorization_lock,
            std::chrono::minutes(5),
            [&authorization] { return authorization->completed; })) {
        target->AbortAuthorize();
        authorization_lock.unlock();
        std::lock_guard auth_lock(auth_mutex);
        if (pending_authorization == authorization) {
            pending_authorization.reset();
        }
        return 3;
    }
    if (!authorization->successful || authorization->code.empty()) {
        authorization_lock.unlock();
        std::lock_guard auth_lock(auth_mutex);
        if (pending_authorization == authorization) {
            pending_authorization.reset();
        }
        return 4;
    }
    const auto code = authorization->code;
    const auto redirect_uri = authorization->redirect_uri;
    const auto code_verifier = authorization->code_verifier;
    authorization_lock.unlock();
    {
        std::lock_guard auth_lock(auth_mutex);
        if (pending_authorization == authorization) {
            pending_authorization.reset();
        }
    }

    return exchange_authorization_code(
        target,
        parsed_id,
        code,
        code_verifier,
        redirect_uri,
        access_token,
        access_token_capacity,
        refresh_token,
        refresh_token_capacity,
        expires_in_seconds);
}

extern "C" __attribute__((visibility("default"))) int drpc_exchange_authorization_code(
    const char* application_id,
    const char* code,
    const char* code_verifier,
    const char* redirect_uri,
    char* access_token,
    int access_token_capacity,
    char* refresh_token,
    int refresh_token_capacity,
    std::int64_t* expires_in_seconds) {
    std::uint64_t parsed_id = 0;
    if (!parse_application_id(application_id, parsed_id) ||
        code == nullptr || *code == '\0' ||
        code_verifier == nullptr || *code_verifier == '\0' ||
        redirect_uri == nullptr || *redirect_uri == '\0') {
        return 1;
    }
    if (drpc_initialize(application_id) != 0) {
        return 2;
    }

    auto target = get_client();
    return exchange_authorization_code(
        target,
        parsed_id,
        safe(code),
        safe(code_verifier),
        safe(redirect_uri),
        access_token,
        access_token_capacity,
        refresh_token,
        refresh_token_capacity,
        expires_in_seconds);
}

extern "C" __attribute__((visibility("default"))) int drpc_authorize(
    const char* application_id,
    char* access_token,
    int access_token_capacity,
    char* refresh_token,
    int refresh_token_capacity,
    std::int64_t* expires_in_seconds) {
    const auto begin_result = drpc_begin_authorize(application_id);
    if (begin_result != 0) {
        return begin_result;
    }
    return drpc_finish_authorize(
        application_id,
        access_token,
        access_token_capacity,
        refresh_token,
        refresh_token_capacity,
        expires_in_seconds);
}

extern "C" __attribute__((visibility("default"))) int drpc_refresh_token(
    const char* application_id,
    const char* current_refresh_token,
    char* access_token,
    int access_token_capacity,
    char* refresh_token,
    int refresh_token_capacity,
    std::int64_t* expires_in_seconds) {
    std::uint64_t parsed_id = 0;
    if (!parse_application_id(application_id, parsed_id) ||
        current_refresh_token == nullptr || *current_refresh_token == '\0') {
        return 1;
    }
    if (drpc_initialize(application_id) != 0) {
        return 2;
    }

    std::lock_guard auth_lock(auth_mutex);
    auto target = get_client();
    if (!target) {
        return 2;
    }

    struct RefreshResult {
        std::mutex mutex;
        std::condition_variable ready;
        bool completed = false;
        bool successful = false;
        std::string access_token;
        std::string refresh_token;
        std::int64_t expires_in = 0;
    };
    auto refresh = std::make_shared<RefreshResult>();
    target->RefreshToken(
        parsed_id,
        safe(current_refresh_token),
        [refresh](
            const discordpp::ClientResult& result,
            std::string new_access_token,
            std::string new_refresh_token,
            discordpp::AuthorizationTokenType,
            std::int32_t expires_in,
            std::string) {
            {
                std::lock_guard result_lock(refresh->mutex);
                refresh->successful = result.Successful();
                refresh->access_token = std::move(new_access_token);
                refresh->refresh_token = std::move(new_refresh_token);
                refresh->expires_in = expires_in;
                refresh->completed = true;
            }
            refresh->ready.notify_one();
        });

    std::unique_lock refresh_lock(refresh->mutex);
    if (!refresh->ready.wait_for(
            refresh_lock,
            std::chrono::seconds(30),
            [&refresh] { return refresh->completed; })) {
        return 3;
    }
    if (!refresh->successful || refresh->access_token.empty() || refresh->refresh_token.empty()) {
        return 4;
    }

    copy_to_buffer(refresh->access_token, access_token, access_token_capacity);
    copy_to_buffer(refresh->refresh_token, refresh_token, refresh_token_capacity);
    if (expires_in_seconds != nullptr) {
        *expires_in_seconds = refresh->expires_in;
    }
    return 0;
}

extern "C" __attribute__((visibility("default"))) int drpc_connect_authenticated(
    const char* application_id,
    const char* access_token) {
    if (access_token == nullptr || *access_token == '\0') {
        return 1;
    }
    if (drpc_initialize(application_id) != 0) {
        return 2;
    }
    return update_token_and_connect(get_client(), safe(access_token));
}

extern "C" __attribute__((visibility("default"))) int drpc_connection_status() {
    auto target = get_client();
    return target ? static_cast<int>(target->GetStatus()) : 0;
}

extern "C" __attribute__((visibility("default"))) int drpc_set_activity(
    const char* details,
    const char* details_url,
    const char* state,
    std::int64_t start_timestamp,
    std::int64_t end_timestamp,
    const char* large_image,
    const char* large_text,
    const char* large_url,
    const char* small_image,
    const char* small_text,
    const char* small_url,
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
    if (details_url != nullptr && *details_url != '\0') {
        activity.SetDetailsUrl(safe(details_url));
    }
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
            if (large_url != nullptr && *large_url != '\0') {
                assets.SetLargeUrl(safe(large_url));
            }
        }
        if (small_image != nullptr && *small_image != '\0') {
            assets.SetSmallImage(safe(small_image));
            assets.SetSmallText(safe(small_text));
            if (small_url != nullptr && *small_url != '\0') {
                assets.SetSmallUrl(safe(small_url));
            }
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

    if (auto current_user = client->GetCurrentUserV2(); current_user.has_value()) {
        copy_to_buffer(std::to_string(current_user->Id()), user_id, user_id_capacity);
        copy_to_buffer(current_user->DisplayName(), display_name, display_name_capacity);
        copy_to_buffer(current_user->Username(), username, username_capacity);
        copy_to_buffer(
            current_user->AvatarUrl(
                discordpp::UserHandle::AvatarType::Png,
                discordpp::UserHandle::AvatarType::Png),
            avatar_url,
            avatar_url_capacity);
        return 0;
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
    if (client) {
        client->Disconnect();
    }
    stop_callbacks();
    client.reset();
    current_application_id = 0;
}
