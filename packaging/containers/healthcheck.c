#include <arpa/inet.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <sys/time.h>
#include <unistd.h>

int main(void)
{
    const char *port_value = getenv("DASHBOARD_PORT");
    char *end = NULL;
    long port = 8080;
    if (port_value != NULL && *port_value != '\0') {
        errno = 0;
        port = strtol(port_value, &end, 10);
        if (errno != 0 || *end != '\0' || port < 1 || port > 65535) {
            fputs("DASHBOARD_PORT must be between 1 and 65535\n", stderr);
            return EXIT_FAILURE;
        }
    }

    int connection = socket(AF_INET, SOCK_STREAM, 0);
    if (connection < 0) {
        perror("socket");
        return EXIT_FAILURE;
    }

    struct timeval timeout = { .tv_sec = 4, .tv_usec = 0 };
    setsockopt(connection, SOL_SOCKET, SO_RCVTIMEO, &timeout, sizeof(timeout));
    setsockopt(connection, SOL_SOCKET, SO_SNDTIMEO, &timeout, sizeof(timeout));

    struct sockaddr_in address = {
        .sin_family = AF_INET,
        .sin_port = htons((unsigned short)port),
        .sin_addr = { .s_addr = htonl(INADDR_LOOPBACK) }
    };
    if (connect(connection, (struct sockaddr *)&address, sizeof(address)) != 0) {
        perror("connect");
        close(connection);
        return EXIT_FAILURE;
    }

    static const char request[] =
        "GET /api/health HTTP/1.1\r\n"
        "Host: 127.0.0.1\r\n"
        "Connection: close\r\n\r\n";
    size_t sent = 0;
    while (sent < sizeof(request) - 1) {
        ssize_t written = send(connection, request + sent, sizeof(request) - 1 - sent, 0);
        if (written <= 0) {
            perror("send");
            close(connection);
            return EXIT_FAILURE;
        }
        sent += (size_t)written;
    }

    char response[16] = { 0 };
    size_t received = 0;
    while (received < 12) {
        ssize_t count = recv(connection, response + received, sizeof(response) - 1 - received, 0);
        if (count <= 0) {
            break;
        }
        received += (size_t)count;
    }
    close(connection);
    if (received < 12 ||
        (strncmp(response, "HTTP/1.1 200", 12) != 0 &&
         strncmp(response, "HTTP/1.0 200", 12) != 0)) {
        fputs("health endpoint did not return HTTP 200\n", stderr);
        return EXIT_FAILURE;
    }

    return EXIT_SUCCESS;
}
