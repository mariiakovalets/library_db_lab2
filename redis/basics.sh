#!/bin/bash
# Basic Redis queries on library examples (item 6).
# Run from the repository root with the containers up:
#   bash redis/basics.sh
# Every command is printed with "> " and followed by Redis's answer.
# All keys start with "lib:", so the script does not touch anything else.

cd "$(dirname "$0")/.." || exit 1

r() {
    local line=">"
    for arg in "$@"; do
        if [[ $arg == *" "* ]]; then line+=" \"$arg\""; else line+=" $arg"; fi
    done
    echo "$line"

    local answer
    answer=$(docker compose exec -T redis redis-cli "$@")
    # redis-cli prints "nothing" (nil) as an empty line
    echo "${answer:-(nil)}"
}

section() {
    echo
    echo "=== $1"
}

# start from a clean state
docker compose exec -T redis redis-cli --scan --pattern 'lib:*' | xargs -r docker compose exec -T redis redis-cli DEL > /dev/null

section "1. String: a value under a key"
r SET lib:book:1:title "Кобзар"
r GET lib:book:1:title

section "2. String with a time limit: a reading room seat is held for 15 minutes"
# NX: set only if the key does not exist yet, so two readers cannot take the same seat
r SET lib:seat:12 "reader:5" EX 900 NX
r SET lib:seat:12 "reader:6" EX 900 NX
r GET lib:seat:12
r TTL lib:seat:12

section "3. Counter: views of a book page"
r INCR lib:book:1:views
r INCR lib:book:1:views
r INCRBY lib:book:1:views 10
r GET lib:book:1:views

section "4. Hash: cached book card, kept for one hour"
r HSET lib:book:1 title "Кобзар" author "Тарас Шевченко" year 2019 available 2
r HGET lib:book:1 author
r HINCRBY lib:book:1 available -1
r HGETALL lib:book:1
r EXPIRE lib:book:1 3600
r TTL lib:book:1

section "5. List: the last books a reader opened, only the 3 most recent are kept"
r LPUSH lib:reader:5:recent "book:1" "book:8" "book:3" "book:6"
r LTRIM lib:reader:5:recent 0 2
r LRANGE lib:reader:5:recent 0 -1

section "6. Set: favourite books, and the ones two readers have in common"
r SADD lib:reader:5:favorites "book:1" "book:3" "book:8"
r SADD lib:reader:6:favorites "book:3" "book:8" "book:4"
r SISMEMBER lib:reader:5:favorites "book:4"
r SINTER lib:reader:5:favorites lib:reader:6:favorites

section "7. Sorted set: top books by number of loans"
r ZINCRBY lib:popular 5 "book:1"
r ZINCRBY lib:popular 3 "book:8"
r ZINCRBY lib:popular 7 "book:3"
r ZINCRBY lib:popular 1 "book:1"
r ZREVRANGE lib:popular 0 2 WITHSCORES
r ZSCORE lib:popular "book:1"

section "8. Expiry in action: a key that lives 2 seconds"
r SET lib:confirm-code "4821" EX 2
r GET lib:confirm-code
echo "(waiting 3 seconds)"
sleep 3
r GET lib:confirm-code
r EXISTS lib:confirm-code

section "9. Working with keys"
r TYPE lib:popular
r TYPE lib:book:1
r SCAN 0 MATCH "lib:reader:*" COUNT 100
r DEL lib:book:1:title
r EXISTS lib:book:1:title
