#include <netinet/in.h>
#include <netinet/tcp.h>
#define check(expr) if (!(expr)) { return 0; }

int enable_keepalive(int sock, int enable_keepalive,int time, int interval,int maxpkt)
{
  check(setsockopt(sock, SOL_SOCKET, SO_KEEPALIVE, &enable_keepalive, sizeof(int)) != -1);
  check(setsockopt(sock, IPPROTO_TCP, TCP_KEEPIDLE, &time, sizeof(int)) != -1);
  check(setsockopt(sock, IPPROTO_TCP, TCP_KEEPINTVL, &interval, sizeof(int)) != -1);
  check(setsockopt(sock, IPPROTO_TCP, TCP_KEEPCNT, &maxpkt, sizeof(int)) != -1);
  return 1;
}
