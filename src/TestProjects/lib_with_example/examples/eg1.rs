use std;
use lib_with_example::add;

fn main() {
    println!("file example: 1 + 2 = {}", add(1, 2));

    let args: Vec<String> = std::env::args().collect();
    println!("command line: {:?}", args);

    let mut user_input = String::new();
    std::io::stdin().read_line(&mut user_input);
}
