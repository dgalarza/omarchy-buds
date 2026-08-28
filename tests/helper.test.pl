#!/usr/bin/perl

use strict;
use warnings;

use File::Spec;
use File::Temp qw(tempdir);
use JSON::PP ();
use POSIX qw(mkfifo);
use Time::HiRes qw(time);

use FindBin;
require File::Spec->catfile($FindBin::Bin, "..", "bin", "omarchy-buds-helper");

my $failures = 0;
my $JSON = JSON::PP->new->canonical(1);
my $directory = tempdir("omarchy-buds-helper-test-XXXXXX", TMPDIR => 1, CLEANUP => 1);

sub check {
    my ($name, $passed) = @_;
    return if $passed;
    $failures++;
    print STDERR "FAIL $name\n";
}

sub write_bytes {
    my ($name, $bytes) = @_;
    my $path = File::Spec->catfile($directory, $name);
    open(my $handle, ">:raw", $path) or die "open $path: $!";
    print {$handle} $bytes;
    close($handle) or die "close $path: $!";
    return $path;
}

sub envelope_for {
    return OmarchyBudsHelper::status_envelope($_[0]);
}

my $valid_path = write_bytes("valid.json", $JSON->encode({
    schema_version => 1,
    process_id => 4242,
    connected => JSON::PP::true,
    device_name => "Test Buds",
    model => "Buds4Pro",
    battery => {
        left => {
            available => JSON::PP::true,
            level => 85,
            charging => JSON::PP::false,
            placement => "Wearing"
        }
    },
    actions => { anc_toggle => "AncToggle" },
    ignored => "discard me"
}));
my $valid = envelope_for($valid_path);
check("valid status is accepted", $valid->{ok} && $valid->{present});
check("valid status keeps bounded identity", $valid->{status}->{device_name} eq "Test Buds");
check("valid status keeps allowed actions", $valid->{status}->{actions}->{anc_toggle} eq "AncToggle");
check("unknown fields are discarded", !exists $valid->{status}->{ignored});
check("canonical envelope stays bounded",
    length(OmarchyBudsHelper::encode_envelope($valid))
        <= OmarchyBudsHelper::MAX_CANONICAL_BYTES());

my $partial_path = write_bytes("partial.json", '{"schema_version":1,"connected":true}');
my $partial = envelope_for($partial_path);
check("partial status is accepted", $partial->{ok});
check("partial status gets a complete battery shape",
    exists $partial->{status}->{battery}->{left}->{available});
check("partial status does not invent an action",
    $partial->{status}->{actions}->{anc_toggle} eq "");

my $missing = envelope_for(File::Spec->catfile($directory, "missing.json"));
check("missing status is absent", !$missing->{ok} && !$missing->{present});
check("missing status uses a fixed error code", $missing->{error} eq "missing");

my $malformed = envelope_for(write_bytes("malformed.json", "{not json"));
check("malformed JSON is rejected", !$malformed->{ok} && $malformed->{error} eq "invalid_json");

my $invalid_utf8 = envelope_for(write_bytes("invalid-utf8.json", "{\"schema_version\":1,\"device_name\":\xFF}"));
check("invalid UTF-8 is rejected", !$invalid_utf8->{ok} && $invalid_utf8->{error} eq "invalid_utf8");

my $target_path = write_bytes("target.json", '{"schema_version":1}');
my $symlink_path = File::Spec->catfile($directory, "status-link.json");
symlink($target_path, $symlink_path) or die "symlink: $!";
my $symlink = envelope_for($symlink_path);
check("symlink status is rejected", !$symlink->{ok} && $symlink->{error} eq "symlink");

my $fifo_path = File::Spec->catfile($directory, "status.fifo");
mkfifo($fifo_path, 0600) or die "mkfifo: $!";
my $fifo_started = time();
my $fifo = envelope_for($fifo_path);
check("FIFO status is rejected", !$fifo->{ok} && $fifo->{error} eq "not_regular");
check("FIFO status does not block", time() - $fifo_started < 1);

my $special = envelope_for("/dev/null");
check("special-file status is rejected", !$special->{ok} && $special->{error} eq "not_regular");

my $oversized = envelope_for(write_bytes(
    "oversized.json",
    '{"schema_version":1,"padding":"' . ("x" x OmarchyBudsHelper::MAX_STATUS_BYTES()) . '"}'
));
check("oversized status is rejected", !$oversized->{ok} && $oversized->{error} eq "oversized");

my $deep_value = "0";
$deep_value = "{\"x\":$deep_value}" for 1 .. (OmarchyBudsHelper::MAX_JSON_DEPTH() + 4);
my $deep = envelope_for(write_bytes("deep.json", $deep_value));
check("deep status is rejected", !$deep->{ok}
    && ($deep->{error} eq "invalid_json" || $deep->{error} eq "too_deep"));

my $long_string = envelope_for(write_bytes(
    "long-string.json",
    $JSON->encode({ schema_version => 1, device_name => "x" x 129 })
));
check("overlong known string is rejected",
    !$long_string->{ok} && $long_string->{error} eq "long_string");

my $wrong_schema = envelope_for(write_bytes("schema.json", '{"schema_version":"1"}'));
check("string schema is rejected",
    !$wrong_schema->{ok} && $wrong_schema->{error} eq "invalid_schema");

my $bounded = OmarchyBudsHelper::run_bounded(
    command => ["/usr/bin/perl", "-e", 'print "x" x 2048'],
    timeout => 1,
    stdout_limit => 64,
    stderr_limit => 64
);
check("oversized command output is stopped", $bounded->{overflow});
check("oversized command output is bounded before collection", length($bounded->{stdout}) <= 64);

my $hung_started = time();
my $hung = OmarchyBudsHelper::run_bounded(
    command => ["/usr/bin/perl", "-e", 'sleep 5'],
    timeout => 0.15,
    stdout_limit => 64,
    stderr_limit => 64
);
check("hung command is terminated", $hung->{timed_out});
check("hung command returns within its ceiling", time() - $hung_started < 2);

my $normal = OmarchyBudsHelper::run_bounded(
    command => ["/usr/bin/perl", "-e", 'print "ok\\n"'],
    timeout => 1,
    stdout_limit => 64,
    stderr_limit => 64
);
check("bounded command preserves small output",
    !$normal->{timed_out} && !$normal->{overflow}
        && $normal->{exit_code} == 0 && $normal->{stdout} eq "ok\n");

if ($failures > 0) {
    print STDERR "$failures helper checks failed\n";
    exit 1;
}

print "helper.test.pl: all checks passed\n";
